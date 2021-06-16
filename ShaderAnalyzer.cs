﻿#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;
using System.Globalization;
using System.Linq;

namespace d4rkpl4y3r
{
    public class ShaderAnalyzer
    {
        public Shader shader;
        public readonly List<string> processedLines = new List<string>();
        public readonly List<string> properties = new List<string>();

        public ShaderAnalyzer()
        {

        }

        public ShaderAnalyzer(Shader shader)
        {
            this.shader = shader;
        }

        public void Parse()
        {
            processedLines.Clear();
            properties.Clear();
            ReadShader();
            ParsePropertyBlock();
            File.WriteAllLines("Assets/d4rkAvatarOptimizer/TrashBin/LastParsedShader.shader", processedLines);
        }

        private static int FindEndOfStringLiteral(string text, int startIndex)
        {
            for (int i = startIndex; i < text.Length; i++)
            {
                if (text[i] == '\\')
                {
                    i++;
                }
                else if (text[i] == '"')
                {
                    return i;
                }
            }
            return -1;
        }

        private bool ReadShader()
        {
            maxIncludes = 50;
            return RecursiveParseFile(AssetDatabase.GetAssetPath(shader), processedLines);
        }

        private static int maxIncludes = 50;
        private static bool RecursiveParseFile(string filePath, List<string> processedLines, List<string> alreadyIncludedFiles = null)
        {
            bool isTopLevelFile = false;
            if (alreadyIncludedFiles == null)
            {
                alreadyIncludedFiles = new List<string>();
                isTopLevelFile = true;
            }
            if (--maxIncludes < 0)
            {
                Debug.LogError("Reach max include depth");
                return false;
            }
            filePath = Path.GetFullPath(filePath);
            if (alreadyIncludedFiles.Contains(filePath))
            {
                return true;
            }
            alreadyIncludedFiles.Add(filePath);
            string[] rawLines = null;
            try
            {
                rawLines = File.ReadAllLines(filePath);
            }
            catch (FileNotFoundException e)
            {
                return false; //this is probably a unity include file
            }
            catch (IOException e)
            {
                Debug.LogError("Error reading shader file.  " + e.ToString());
                return false;
            }

            for (int lineIndex = 0; lineIndex < rawLines.Length; lineIndex++)
            {
                string trimmedLine = rawLines[lineIndex].Trim();
                if (trimmedLine == "")
                    continue;
                for (int i = 0; i < trimmedLine.Length - 1; i++)
                {
                    if (trimmedLine[i] == '"')
                    {
                        int end = FindEndOfStringLiteral(trimmedLine, i + 1);
                        i = (end == -1) ? trimmedLine.Length : end;
                        continue;
                    }
                    else if (trimmedLine[i] != '/')
                    {
                        continue;
                    }
                    if (trimmedLine[i + 1] == '/')
                    {
                        trimmedLine = trimmedLine.Substring(0, i).TrimEnd();
                        break;
                    }
                    else if (trimmedLine[i + 1] == '*')
                    {
                        int endCommentBlock = trimmedLine.IndexOf("*/", i + 2);
                        bool isMultiLineCommentBlock = endCommentBlock == -1;
                        while (endCommentBlock == -1 && ++lineIndex < rawLines.Length)
                        {
                            endCommentBlock = rawLines[lineIndex].IndexOf("*/");
                        }
                        if (endCommentBlock != -1)
                        {
                            if (isMultiLineCommentBlock)
                            {
                                trimmedLine = trimmedLine.Substring(0, i)
                                    + rawLines[lineIndex].Substring(endCommentBlock + 2);
                            }
                            else
                            {
                                trimmedLine = trimmedLine.Substring(0, i)
                                    + trimmedLine.Substring(endCommentBlock + 2);
                            }
                            i -= 1;
                        }
                        else
                        {
                            trimmedLine = trimmedLine.Substring(0, i).TrimEnd();
                            break;
                        }
                    }
                }
                if (trimmedLine == "")
                    continue;
                if (isTopLevelFile && (trimmedLine == "CGINCLUDE" || trimmedLine == "CGPROGRAM"))
                {
                    alreadyIncludedFiles.Clear();
                }
                if (trimmedLine.StartsWith("#include "))
                {
                    int firstQuote = trimmedLine.IndexOf('"');
                    int lastQuote = trimmedLine.LastIndexOf('"');
                    string includePath = trimmedLine.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                    includePath = Path.GetDirectoryName(filePath) + "/" + includePath;
                    if (!RecursiveParseFile(includePath, processedLines, alreadyIncludedFiles))
                    {
                        processedLines.Add(trimmedLine);
                    }
                    continue;
                }
                if (trimmedLine.EndsWith("{"))
                {
                    trimmedLine = trimmedLine.Substring(0, trimmedLine.Length - 1).TrimEnd();
                    if (trimmedLine != "")
                        processedLines.Add(trimmedLine);
                    processedLines.Add("{");
                    continue;
                }
                processedLines.Add(trimmedLine);
            }
            return true;
        }

        private void ParsePropertyBlock()
        {
            bool isInPropertyBlock = false;
            int propertyBlockBraceDepth = -1;
            int braceDepth = 0;
            for (int lineIndex = 0; lineIndex < processedLines.Count; lineIndex++)
            {
                string line = processedLines[lineIndex];
                if (line == "{")
                {
                    braceDepth++;
                }
                else if (line == "}")
                {
                    braceDepth--;
                    if (isInPropertyBlock && braceDepth == propertyBlockBraceDepth)
                    {
                        isInPropertyBlock = false;
                        return;
                    }
                }
                else if (line == "Properties" && processedLines[lineIndex + 1] == "{")
                {
                    isInPropertyBlock = true;
                    propertyBlockBraceDepth = braceDepth;
                    braceDepth++;
                    lineIndex++;
                }
                else if (isInPropertyBlock)
                {
                    string modifiedLine = line;
                    int openBracketIndex = line.IndexOf('[');
                    while (openBracketIndex != -1)
                    {
                        int closeBracketIndex = modifiedLine.IndexOf(']') + 1;
                        if (closeBracketIndex != 0)
                        {
                            modifiedLine = modifiedLine.Substring(0, openBracketIndex)
                                + modifiedLine.Substring(closeBracketIndex);
                            openBracketIndex = modifiedLine.IndexOf('[');
                        }
                        else
                        {
                            break;
                        }
                    }
                    modifiedLine = modifiedLine.Trim();
                    int parentheses = modifiedLine.IndexOf('(');
                    if (parentheses != -1)
                    {
                        properties.Add(modifiedLine.Substring(0, parentheses).TrimEnd());
                    }
                }
            }
        }
    }
}
#endif