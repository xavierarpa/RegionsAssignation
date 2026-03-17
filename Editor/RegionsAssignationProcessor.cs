/*
Copyright (c) 2026 Xavier Arpa López Thomas Peter ('xavierarpa')

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RegionsAssignation.Editor
{
    internal static class RegionsAssignationProcessor
    {
        private static readonly Regex typeRegex = new Regex(
            @"\b(class|struct|record)\s+(?<name>@?[A-Za-z_][A-Za-z0-9_]*)[^;{]*\{",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex sanitizeRegex = new Regex(
            "//.*?$|/\\*.*?\\*/|@\"(?:[^\"]|\"\")*\"|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline);

        private static readonly Regex attributeRegex = new Regex(
            @"\[\s*(?:\w+\s*:\s*)?(?<attr>[A-Za-z_][A-Za-z0-9_\.]*)",
            RegexOptions.Compiled);

        private static readonly Regex nestedTypeRegex = new Regex(
            @"^(?:\b(?:public|private|protected|internal|static|sealed|abstract|partial|new|unsafe|readonly|ref|volatile|virtual|extern|async)\b\s+)*(class|struct|interface|enum|record)\s+(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex whitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        private static readonly HashSet<string> unityLifecycleMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Awake",
            "OnEnable",
            "Start",
            "Update",
            "LateUpdate",
            "FixedUpdate",
            "OnDisable",
            "OnDestroy",
            "Reset",
            "OnValidate",
            "OnGUI",
            "OnApplicationFocus",
            "OnApplicationPause",
            "OnApplicationQuit",
            "OnBecameVisible",
            "OnBecameInvisible",
            "OnWillRenderObject",
            "OnPreCull",
            "OnPreRender",
            "OnPostRender",
            "OnRenderObject",
            "OnRenderImage",
            "OnDrawGizmos",
            "OnDrawGizmosSelected",
            "OnTransformChildrenChanged",
            "OnTransformParentChanged",
            "OnRectTransformDimensionsChange",
            "OnCanvasGroupChanged",
            "OnCanvasHierarchyChanged",
            "OnBeforeTransformParentChanged",
            "OnDidApplyAnimationProperties"
        };

        private static readonly string[] unityLifecyclePrefixes =
        {
            "OnCollision",
            "OnTrigger",
            "OnControllerCollider",
            "OnAnimator",
            "OnMouse",
            "OnParticle",
            "OnJoint",
            "OnConnected",
            "OnDisconnected"
        };

        internal static RegionsAssignationScriptResult ProcessFile(
            string filePath,
            IReadOnlyList<RegionsAssignationRule> rules,
            bool createUnassignedRegion,
            string unassignedRegionName,
            bool cleanExistingRegionsBeforeProcessing)
        {
            var result = new RegionsAssignationScriptResult
            {
                FilePath = filePath,
                IsSelected = true,
                IsSuccess = false,
                HasChanges = false
            };

            string fileContent;
            try
            {
                fileContent = File.ReadAllText(filePath);
            }
            catch (Exception exception)
            {
                result.Message = $"No se pudo leer el archivo: {exception.Message}";
                return result;
            }

            result.OriginalContent = fileContent;

            string content = cleanExistingRegionsBeforeProcessing
                ? RemoveRegionDirectives(fileContent)
                : fileContent;

            var diagnostics = new List<string>();
            var runtimeRules = BuildRuntimeRules(rules, diagnostics);

            if (runtimeRules.Count == 0)
            {
                result.Message = "No hay reglas válidas habilitadas para procesar.";
                return result;
            }

            string sanitizedContent = Sanitize(content);
            if (!TryFindFirstType(sanitizedContent, out string typeName, out int openBraceIndex, out int closeBraceIndex))
            {
                result.Message = "No se encontró una class/struct/record para reordenar miembros.";
                return result;
            }

            int bodyStart = openBraceIndex + 1;
            int bodyLength = closeBraceIndex - bodyStart;
            if (bodyLength < 0)
            {
                result.Message = "No se pudo determinar el cuerpo del tipo a procesar.";
                return result;
            }

            string body = content.Substring(bodyStart, bodyLength);
            string sanitizedBody = sanitizedContent.Substring(bodyStart, bodyLength);

            if (!TryExtractMembers(body, sanitizedBody, typeName, out List<RegionsAssignationMemberInfo> members, out string parseError))
            {
                result.Message = parseError;
                return result;
            }

            result.MemberCount = members.Count;
            if (members.Count == 0)
            {
                result.IsSuccess = true;
                result.PreviewContent = content;
                result.Message = "No se encontraron miembros de nivel superior para ordenar.";
                return result;
            }

            var assignedByRegion = new Dictionary<string, List<RegionsAssignationMemberInfo>>(StringComparer.OrdinalIgnoreCase);
            var unassignedMembers = new List<RegionsAssignationMemberInfo>();

            foreach (var member in members)
            {
                RuntimeRule matchedRule = null;
                foreach (var runtimeRule in runtimeRules)
                {
                    if (Matches(runtimeRule, member))
                    {
                        matchedRule = runtimeRule;
                        break;
                    }
                }

                if (matchedRule == null)
                {
                    unassignedMembers.Add(member);
                    continue;
                }

                if (!assignedByRegion.TryGetValue(matchedRule.RegionName, out List<RegionsAssignationMemberInfo> regionMembers))
                {
                    regionMembers = new List<RegionsAssignationMemberInfo>();
                    assignedByRegion[matchedRule.RegionName] = regionMembers;
                }

                regionMembers.Add(member);
            }

            result.AssignedCount = members.Count - unassignedMembers.Count;

            string newline = DetectNewLine(content);
            string indent = DetectTopLevelIndent(members);
            string generatedBody = BuildBody(
                runtimeRules,
                assignedByRegion,
                unassignedMembers,
                createUnassignedRegion,
                unassignedRegionName,
                newline,
                indent,
                result);

            string generatedContent = string.Concat(
                content.Substring(0, bodyStart),
                generatedBody,
                content.Substring(closeBraceIndex));

            result.PreviewContent = generatedContent;
            result.HasChanges = !string.Equals(fileContent, generatedContent, StringComparison.Ordinal);
            result.IsSuccess = true;

            if (diagnostics.Count > 0)
            {
                result.Message = string.Join("\n", diagnostics);
            }
            else if (!result.HasChanges)
            {
                result.Message = "El archivo ya cumple con la distribución de regiones configurada.";
            }
            else
            {
                result.Message = "Preview generado correctamente.";
            }

            return result;
        }

        private static List<RuntimeRule> BuildRuntimeRules(
            IReadOnlyList<RegionsAssignationRule> rules,
            List<string> diagnostics)
        {
            var runtimeRules = new List<RuntimeRule>();
            if (rules == null)
            {
                return runtimeRules;
            }

            for (int index = 0; index < rules.Count; index++)
            {
                RegionsAssignationRule rule = rules[index];
                if (rule == null || !rule.IsEnabled)
                {
                    continue;
                }

                string regionName = rule.RegionName == null
                    ? string.Empty
                    : rule.RegionName.Trim();

                if (regionName.Length == 0)
                {
                    continue;
                }

                Regex compiledRegex = null;
                if (!string.IsNullOrWhiteSpace(rule.NameRegex))
                {
                    try
                    {
                        compiledRegex = new Regex(rule.NameRegex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    }
                    catch (Exception exception)
                    {
                        diagnostics.Add($"Regla '{regionName}': NameRegex inválido ({exception.Message}).");
                        continue;
                    }
                }

                runtimeRules.Add(new RuntimeRule(
                    index,
                    rule.Priority,
                    regionName,
                    rule.MemberKinds,
                    rule.MatchOverrideMethods,
                    rule.MatchUnityLifecycleMethods,
                    SplitTokens(rule.NameStartsWith),
                    SplitTokens(rule.NameContains),
                    compiledRegex,
                    SplitTokens(rule.AttributeContains)));
            }

            runtimeRules.Sort((left, right) =>
            {
                int priorityCompare = right.Priority.CompareTo(left.Priority);
                if (priorityCompare != 0)
                {
                    return priorityCompare;
                }

                return left.Order.CompareTo(right.Order);
            });

            return runtimeRules;
        }

        private static bool TryFindFirstType(
            string sanitizedContent,
            out string typeName,
            out int openBraceIndex,
            out int closeBraceIndex)
        {
            typeName = string.Empty;
            openBraceIndex = -1;
            closeBraceIndex = -1;

            Match match = typeRegex.Match(sanitizedContent);
            if (!match.Success)
            {
                return false;
            }

            typeName = CleanIdentifier(match.Groups["name"].Value);
            openBraceIndex = match.Index + match.Value.LastIndexOf('{');

            if (!TryFindMatchingBrace(sanitizedContent, openBraceIndex, out closeBraceIndex))
            {
                return false;
            }

            return true;
        }

        private static bool TryFindMatchingBrace(string text, int openBraceIndex, out int closeBraceIndex)
        {
            closeBraceIndex = -1;
            int depth = 0;

            for (int index = openBraceIndex; index < text.Length; index++)
            {
                char current = text[index];
                if (current == '{')
                {
                    depth++;
                    continue;
                }

                if (current != '}')
                {
                    continue;
                }

                depth--;
                if (depth == 0)
                {
                    closeBraceIndex = index;
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractMembers(
            string body,
            string sanitizedBody,
            string typeName,
            out List<RegionsAssignationMemberInfo> members,
            out string error)
        {
            members = new List<RegionsAssignationMemberInfo>();
            error = string.Empty;

            int index = 0;
            while (index < body.Length)
            {
                index = SkipWhitespaceAndRegionDirectives(body, index, out string unsupportedDirective);
                if (!string.IsNullOrEmpty(unsupportedDirective))
                {
                    error = $"Hay una directiva de preprocesador no soportada a nivel de tipo ({unsupportedDirective}).";
                    return false;
                }

                if (index >= body.Length)
                {
                    break;
                }

                int memberStart = index;
                while (memberStart > 0 && body[memberStart - 1] != '\n' && body[memberStart - 1] != '\r')
                {
                    memberStart--;
                }

                if (!TryReadMemberEnd(sanitizedBody, memberStart, out int memberEnd))
                {
                    error = "No se pudo detectar de forma segura el final de un miembro. Archivo omitido para evitar cambios incorrectos.";
                    return false;
                }

                int lineEnd = FindLineEnd(body, memberEnd);
                if (lineEnd < memberStart)
                {
                    lineEnd = memberEnd;
                }

                string memberText = body.Substring(memberStart, lineEnd - memberStart + 1);
                members.Add(CreateMemberInfo(memberText, typeName));
                index = lineEnd + 1;
            }

            return true;
        }

        private static int SkipWhitespaceAndRegionDirectives(string text, int startIndex, out string unsupportedDirective)
        {
            unsupportedDirective = string.Empty;
            int index = startIndex;

            while (index < text.Length)
            {
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                if (index >= text.Length || text[index] != '#')
                {
                    break;
                }

                int lineEnd = FindLineEnd(text, index);
                string directive = text.Substring(index, lineEnd - index + 1).Trim();
                if (directive.StartsWith("#region", StringComparison.Ordinal) ||
                    directive.StartsWith("#endregion", StringComparison.Ordinal))
                {
                    index = lineEnd + 1;
                    continue;
                }

                unsupportedDirective = directive;
                break;
            }

            return index;
        }

        private static bool TryReadMemberEnd(string sanitizedBody, int startIndex, out int endIndex)
        {
            endIndex = -1;
            int depth = 0;

            for (int index = startIndex; index < sanitizedBody.Length; index++)
            {
                char current = sanitizedBody[index];

                if (current == '{')
                {
                    depth++;
                    continue;
                }

                if (current == '}')
                {
                    if (depth == 0)
                    {
                        return false;
                    }

                    depth--;
                    if (depth == 0)
                    {
                        int nextToken = SkipWhitespace(sanitizedBody, index + 1);
                        if (nextToken < sanitizedBody.Length &&
                            (sanitizedBody[nextToken] == ';' || sanitizedBody[nextToken] == '='))
                        {
                            continue;
                        }

                        endIndex = index;
                        return true;
                    }

                    continue;
                }

                if (current == ';' && depth == 0)
                {
                    endIndex = index;
                    return true;
                }
            }

            return false;
        }

        private static int SkipWhitespace(string text, int startIndex)
        {
            int index = startIndex;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            return index;
        }

        private static int FindLineEnd(string text, int index)
        {
            int lineEnd = index;
            while (lineEnd < text.Length && text[lineEnd] != '\n' && text[lineEnd] != '\r')
            {
                lineEnd++;
            }

            return lineEnd > index ? lineEnd - 1 : index;
        }

        private static RegionsAssignationMemberInfo CreateMemberInfo(string memberText, string typeName)
        {
            string sanitizedMember = Sanitize(memberText);
            string declarationWithoutTrivia = RemoveLeadingTrivia(sanitizedMember);
            string signature = ExtractSignature(declarationWithoutTrivia);
            string normalizedSignature = CollapseWhitespace(signature).Trim();
            string normalizedDeclaration = CollapseWhitespace(declarationWithoutTrivia).Trim();

            RegionsAssignationMemberKind memberKind = DetermineMemberKind(
                normalizedSignature,
                normalizedDeclaration,
                typeName,
                out string memberName);

            bool isOverride = ContainsWord(normalizedSignature, "override");
            bool isUnityLifecycleMethod = memberKind == RegionsAssignationMemberKind.Method &&
                                          IsUnityLifecycleMethod(memberName);

            IReadOnlyList<string> attributeNames = ExtractAttributeNames(memberText);

            return new RegionsAssignationMemberInfo(
                memberText,
                memberKind,
                memberName,
                isOverride,
                isUnityLifecycleMethod,
                attributeNames);
        }

        private static RegionsAssignationMemberKind DetermineMemberKind(
            string signature,
            string declaration,
            string typeName,
            out string memberName)
        {
            memberName = string.Empty;
            if (string.IsNullOrWhiteSpace(signature))
            {
                return RegionsAssignationMemberKind.Unknown;
            }

            Match nestedTypeMatch = nestedTypeRegex.Match(signature);
            if (nestedTypeMatch.Success)
            {
                memberName = CleanIdentifier(nestedTypeMatch.Groups["name"].Value);
                return RegionsAssignationMemberKind.NestedType;
            }

            if (ContainsWord(signature, "delegate"))
            {
                memberName = ExtractTrailingIdentifier(signature, true);
                return RegionsAssignationMemberKind.NestedType;
            }

            if (ContainsWord(signature, "event"))
            {
                memberName = ExtractTrailingIdentifier(signature, true);
                return RegionsAssignationMemberKind.Event;
            }

            int assignmentIndex = FindAssignmentOperatorIndex(signature);
            string declarationSignature = assignmentIndex >= 0
                ? signature.Substring(0, assignmentIndex).TrimEnd()
                : signature;

            int parenthesisIndex = declarationSignature.IndexOf('(');
            if (parenthesisIndex >= 0)
            {
                memberName = ExtractMethodName(declarationSignature, parenthesisIndex);
                if (string.Equals(memberName, typeName, StringComparison.Ordinal))
                {
                    return RegionsAssignationMemberKind.Constructor;
                }

                return RegionsAssignationMemberKind.Method;
            }

            bool hasPropertyAccessors = ContainsWord(declaration, "get") ||
                                        ContainsWord(declaration, "set") ||
                                        ContainsWord(declaration, "init");

            bool isExpressionBodiedProperty = parenthesisIndex < 0 &&
                                              assignmentIndex < 0 &&
                                              signature.IndexOf("=>", StringComparison.Ordinal) >= 0;

            if (hasPropertyAccessors ||
                isExpressionBodiedProperty ||
                signature.IndexOf("this[", StringComparison.Ordinal) >= 0)
            {
                memberName = signature.IndexOf("this[", StringComparison.Ordinal) >= 0
                    ? "this"
                    : ExtractTrailingIdentifier(declarationSignature, false);

                return RegionsAssignationMemberKind.Property;
            }

            memberName = ExtractTrailingIdentifier(declarationSignature, true);
            if (memberName.Length == 0)
            {
                return RegionsAssignationMemberKind.Unknown;
            }

            return RegionsAssignationMemberKind.Field;
        }

        private static string ExtractMethodName(string signature, int parenthesisIndex)
        {
            string beforeParenthesis = signature.Substring(0, parenthesisIndex).Trim();
            if (beforeParenthesis.Length == 0)
            {
                return string.Empty;
            }

            string[] tokens = beforeParenthesis.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return string.Empty;
            }

            string nameToken = tokens[tokens.Length - 1];
            int explicitInterfaceDot = nameToken.LastIndexOf('.');
            if (explicitInterfaceDot >= 0 && explicitInterfaceDot < nameToken.Length - 1)
            {
                nameToken = nameToken.Substring(explicitInterfaceDot + 1);
            }

            int genericIndex = nameToken.IndexOf('<');
            if (genericIndex > 0)
            {
                nameToken = nameToken.Substring(0, genericIndex);
            }

            return CleanIdentifier(nameToken);
        }

        private static string ExtractTrailingIdentifier(string signature, bool preferFirstVariable)
        {
            string leftSide = signature;

            int assignmentIndex = FindAssignmentOperatorIndex(leftSide);
            if (assignmentIndex >= 0)
            {
                leftSide = leftSide.Substring(0, assignmentIndex);
            }

            if (leftSide.IndexOf("this[", StringComparison.Ordinal) >= 0)
            {
                return "this";
            }

            if (preferFirstVariable)
            {
                string[] declarations = leftSide.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (declarations.Length > 0)
                {
                    leftSide = declarations[0];
                }
            }

            string[] tokens = leftSide.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return string.Empty;
            }

            string nameToken = tokens[tokens.Length - 1];
            int dotIndex = nameToken.LastIndexOf('.');
            if (dotIndex >= 0 && dotIndex < nameToken.Length - 1)
            {
                nameToken = nameToken.Substring(dotIndex + 1);
            }

            return CleanIdentifier(nameToken);
        }

        private static int FindAssignmentOperatorIndex(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return -1;
            }

            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] != '=')
                {
                    continue;
                }

                char previous = index > 0 ? value[index - 1] : '\0';
                char next = index < value.Length - 1 ? value[index + 1] : '\0';

                bool isComparisonOrArrow = next == '=' ||
                                           previous == '=' ||
                                           next == '>' ||
                                           previous == '<' ||
                                           previous == '>' ||
                                           previous == '!';

                if (isComparisonOrArrow)
                {
                    continue;
                }

                return index;
            }

            return -1;
        }

        private static IReadOnlyList<string> ExtractAttributeNames(string memberText)
        {
            string header = memberText.Length <= 1200
                ? memberText
                : memberText.Substring(0, 1200);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MatchCollection matches = attributeRegex.Matches(header);
            foreach (Match match in matches)
            {
                string attribute = match.Groups["attr"].Value;
                if (attribute.Length == 0)
                {
                    continue;
                }

                int dotIndex = attribute.LastIndexOf('.');
                if (dotIndex >= 0 && dotIndex < attribute.Length - 1)
                {
                    attribute = attribute.Substring(dotIndex + 1);
                }

                if (attribute.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase))
                {
                    attribute = attribute.Substring(0, attribute.Length - "Attribute".Length);
                }

                attribute = CleanIdentifier(attribute);
                if (attribute.Length == 0)
                {
                    continue;
                }

                names.Add(attribute);
            }

            return names.ToList();
        }

        private static string RemoveLeadingTrivia(string sanitizedMember)
        {
            int index = 0;
            while (index < sanitizedMember.Length)
            {
                while (index < sanitizedMember.Length && char.IsWhiteSpace(sanitizedMember[index]))
                {
                    index++;
                }

                if (index >= sanitizedMember.Length || sanitizedMember[index] != '[')
                {
                    break;
                }

                if (!TryFindMatchingSquareBracket(sanitizedMember, index, out int endIndex))
                {
                    break;
                }

                index = endIndex + 1;
            }

            return index >= sanitizedMember.Length
                ? string.Empty
                : sanitizedMember.Substring(index);
        }

        private static bool TryFindMatchingSquareBracket(string text, int startIndex, out int endIndex)
        {
            endIndex = -1;
            int depth = 0;

            for (int index = startIndex; index < text.Length; index++)
            {
                if (text[index] == '[')
                {
                    depth++;
                    continue;
                }

                if (text[index] != ']')
                {
                    continue;
                }

                depth--;
                if (depth == 0)
                {
                    endIndex = index;
                    return true;
                }
            }

            return false;
        }

        private static string ExtractSignature(string declaration)
        {
            int parenthesisDepth = 0;
            int bracketDepth = 0;

            for (int index = 0; index < declaration.Length; index++)
            {
                char current = declaration[index];

                if (current == '(')
                {
                    parenthesisDepth++;
                    continue;
                }

                if (current == ')')
                {
                    parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
                    continue;
                }

                if (current == '[')
                {
                    bracketDepth++;
                    continue;
                }

                if (current == ']')
                {
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    continue;
                }

                if (parenthesisDepth == 0 && bracketDepth == 0)
                {
                    if (current == ';' || current == '{')
                    {
                        return declaration.Substring(0, index);
                    }
                }
            }

            return declaration;
        }

        private static bool Matches(RuntimeRule rule, RegionsAssignationMemberInfo member)
        {
            if (rule.MemberKinds == RegionsAssignationMemberKind.None)
            {
                return false;
            }

            if (rule.MemberKinds != RegionsAssignationMemberKind.Any && !rule.MemberKinds.HasFlag(member.Kind))
            {
                return false;
            }

            if (rule.MatchOverrideMethods || rule.MatchUnityLifecycleMethods)
            {
                bool specialMatch = false;
                if (rule.MatchOverrideMethods && member.IsOverride)
                {
                    specialMatch = true;
                }

                if (rule.MatchUnityLifecycleMethods && member.IsUnityLifecycleMethod)
                {
                    specialMatch = true;
                }

                if (!specialMatch)
                {
                    return false;
                }
            }

            if (rule.NameStartsWithTokens.Length > 0)
            {
                if (string.IsNullOrEmpty(member.Name) || !MatchesPrefixTokens(rule.NameStartsWithTokens, member.Name))
                {
                    return false;
                }
            }

            if (rule.NameContainsTokens.Length > 0)
            {
                if (string.IsNullOrEmpty(member.Name) || !MatchesContainsTokens(rule.NameContainsTokens, member.Name))
                {
                    return false;
                }
            }

            if (rule.NameRegex != null)
            {
                if (string.IsNullOrEmpty(member.Name) || !rule.NameRegex.IsMatch(member.Name))
                {
                    return false;
                }
            }

            if (rule.AttributeContainsTokens.Length > 0)
            {
                if (!MatchesAttributeTokens(rule.AttributeContainsTokens, member.AttributeNames))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesPrefixTokens(IReadOnlyList<string> tokens, string value)
        {
            for (int index = 0; index < tokens.Count; index++)
            {
                if (value.StartsWith(tokens[index], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesContainsTokens(IReadOnlyList<string> tokens, string value)
        {
            for (int index = 0; index < tokens.Count; index++)
            {
                if (value.IndexOf(tokens[index], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesAttributeTokens(IReadOnlyList<string> tokens, IReadOnlyList<string> attributes)
        {
            if (attributes == null || attributes.Count == 0)
            {
                return false;
            }

            for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
            {
                string token = tokens[tokenIndex];
                for (int attributeIndex = 0; attributeIndex < attributes.Count; attributeIndex++)
                {
                    if (attributes[attributeIndex].IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string BuildBody(
            IReadOnlyList<RuntimeRule> runtimeRules,
            Dictionary<string, List<RegionsAssignationMemberInfo>> assignedByRegion,
            List<RegionsAssignationMemberInfo> unassignedMembers,
            bool createUnassignedRegion,
            string unassignedRegionName,
            string newline,
            string indent,
            RegionsAssignationScriptResult result)
        {
            var orderedRegions = new List<string>();
            foreach (var runtimeRule in runtimeRules.OrderBy(r => r.Order))
            {
                if (!assignedByRegion.ContainsKey(runtimeRule.RegionName))
                {
                    continue;
                }

                if (orderedRegions.Any(region =>
                        string.Equals(region, runtimeRule.RegionName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                orderedRegions.Add(runtimeRule.RegionName);
            }

            var builder = new StringBuilder();
            builder.Append(newline);

            bool hasContent = false;
            foreach (string regionName in orderedRegions)
            {
                if (!assignedByRegion.TryGetValue(regionName, out List<RegionsAssignationMemberInfo> members) || members.Count == 0)
                {
                    continue;
                }

                if (hasContent)
                {
                    builder.Append(newline);
                }

                AppendRegionBlock(builder, regionName, members, indent, newline);
                result.RegionGroups.Add(new RegionsAssignationRegionGroup(
                    regionName,
                    members.Select(GetPreviewMemberName).ToList()));
                hasContent = true;
            }

            if (unassignedMembers.Count > 0)
            {
                string fallbackRegionName = string.IsNullOrWhiteSpace(unassignedRegionName)
                    ? "Unassigned"
                    : unassignedRegionName.Trim();

                if (hasContent)
                {
                    builder.Append(newline);
                }

                if (createUnassignedRegion)
                {
                    AppendRegionBlock(builder, fallbackRegionName, unassignedMembers, indent, newline);
                    result.RegionGroups.Add(new RegionsAssignationRegionGroup(
                        fallbackRegionName,
                        unassignedMembers.Select(GetPreviewMemberName).ToList()));
                }
                else
                {
                    AppendMembers(builder, unassignedMembers, newline);
                    result.RegionGroups.Add(new RegionsAssignationRegionGroup(
                        "Sin región",
                        unassignedMembers.Select(GetPreviewMemberName).ToList()));
                }

                hasContent = true;
            }

            if (!hasContent)
            {
                return newline;
            }

            return builder.ToString();
        }

        private static string GetPreviewMemberName(RegionsAssignationMemberInfo member)
        {
            if (!string.IsNullOrWhiteSpace(member.Name))
            {
                return member.Name;
            }

            return member.Kind.ToString();
        }

        private static void AppendRegionBlock(
            StringBuilder builder,
            string regionName,
            IReadOnlyList<RegionsAssignationMemberInfo> members,
            string indent,
            string newline)
        {
            builder.Append(indent)
                .Append("#region ")
                .Append(regionName)
                .Append(newline)
                .Append(newline);

            AppendMembers(builder, members, newline);

            builder.Append(indent)
                .Append("#endregion")
                .Append(newline);
        }

        private static void AppendMembers(
            StringBuilder builder,
            IReadOnlyList<RegionsAssignationMemberInfo> members,
            string newline)
        {
            for (int index = 0; index < members.Count; index++)
            {
                string content = TrimEdgeBlankLines(members[index].SourceText);
                if (content.Length == 0)
                {
                    continue;
                }

                builder.Append(content);
                builder.Append(newline);

                if (index < members.Count - 1)
                {
                    builder.Append(newline);
                }
            }
        }

        private static string TrimEdgeBlankLines(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            int start = 0;
            int end = value.Length;

            while (start < end && (value[start] == '\r' || value[start] == '\n'))
            {
                start++;
            }

            while (end > start)
            {
                char current = value[end - 1];
                if (current == '\r' || current == '\n' || current == ' ' || current == '\t')
                {
                    end--;
                    continue;
                }

                break;
            }

            return start >= end
                ? string.Empty
                : value.Substring(start, end - start);
        }

        private static string DetectTopLevelIndent(IReadOnlyList<RegionsAssignationMemberInfo> members)
        {
            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                string[] lines = members[memberIndex].SourceText.Replace("\r\n", "\n").Split('\n');
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    int indentCount = 0;
                    while (indentCount < line.Length && (line[indentCount] == ' ' || line[indentCount] == '\t'))
                    {
                        indentCount++;
                    }

                    if (indentCount > 0)
                    {
                        return line.Substring(0, indentCount);
                    }

                    break;
                }
            }

            return "    ";
        }

        private static bool ContainsWord(string value, string word)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(word))
            {
                return false;
            }

            return Regex.IsMatch(value, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
        }

        private static bool IsUnityLifecycleMethod(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            if (unityLifecycleMethods.Contains(methodName))
            {
                return true;
            }

            for (int index = 0; index < unityLifecyclePrefixes.Length; index++)
            {
                if (methodName.StartsWith(unityLifecyclePrefixes[index], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] SplitTokens(string rawTokens)
        {
            if (string.IsNullOrWhiteSpace(rawTokens))
            {
                return Array.Empty<string>();
            }

            return rawTokens
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .Where(token => token.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string DetectNewLine(string value)
        {
            return value.Contains("\r\n", StringComparison.Ordinal)
                ? "\r\n"
                : "\n";
        }

        private static string RemoveRegionDirectives(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content;
            }

            string newline = DetectNewLine(content);
            string normalized = content.Replace("\r\n", "\n");
            string[] lines = normalized.Split('\n');
            var keptLines = new List<string>(lines.Length);

            for (int index = 0; index < lines.Length; index++)
            {
                string trimmed = lines[index].TrimStart();
                if (trimmed.StartsWith("#region", StringComparison.Ordinal) ||
                    trimmed.StartsWith("#endregion", StringComparison.Ordinal))
                {
                    continue;
                }

                keptLines.Add(lines[index]);
            }

            return string.Join(newline, keptLines);
        }

        private static string CollapseWhitespace(string value)
        {
            return whitespaceRegex.Replace(value, " ");
        }

        private static string CleanIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string cleaned = value.Trim();

            if (cleaned.StartsWith("@", StringComparison.Ordinal))
            {
                cleaned = cleaned.Substring(1);
            }

            if (cleaned.StartsWith("~", StringComparison.Ordinal))
            {
                cleaned = cleaned.Substring(1);
            }

            while (cleaned.Length > 0)
            {
                char last = cleaned[cleaned.Length - 1];
                if (char.IsLetterOrDigit(last) || last == '_')
                {
                    break;
                }

                cleaned = cleaned.Substring(0, cleaned.Length - 1);
            }

            return cleaned;
        }

        private static string Sanitize(string content)
        {
            return sanitizeRegex.Replace(content, match => ReplaceWithWhitespace(match.Value));
        }

        private static string ReplaceWithWhitespace(string value)
        {
            char[] chars = value.ToCharArray();
            for (int index = 0; index < chars.Length; index++)
            {
                if (chars[index] != '\r' && chars[index] != '\n')
                {
                    chars[index] = ' ';
                }
            }

            return new string(chars);
        }

        private sealed class RuntimeRule
        {
            internal RuntimeRule(
                int order,
                int priority,
                string regionName,
                RegionsAssignationMemberKind memberKinds,
                bool matchOverrideMethods,
                bool matchUnityLifecycleMethods,
                string[] nameStartsWithTokens,
                string[] nameContainsTokens,
                Regex nameRegex,
                string[] attributeContainsTokens)
            {
                Order = order;
                Priority = priority;
                RegionName = regionName;
                MemberKinds = memberKinds;
                MatchOverrideMethods = matchOverrideMethods;
                MatchUnityLifecycleMethods = matchUnityLifecycleMethods;
                NameStartsWithTokens = nameStartsWithTokens;
                NameContainsTokens = nameContainsTokens;
                NameRegex = nameRegex;
                AttributeContainsTokens = attributeContainsTokens;
            }

            internal int Order { get; }
            internal int Priority { get; }
            internal string RegionName { get; }
            internal RegionsAssignationMemberKind MemberKinds { get; }
            internal bool MatchOverrideMethods { get; }
            internal bool MatchUnityLifecycleMethods { get; }
            internal string[] NameStartsWithTokens { get; }
            internal string[] NameContainsTokens { get; }
            internal Regex NameRegex { get; }
            internal string[] AttributeContainsTokens { get; }
        }
    }
}
