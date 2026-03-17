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
using UnityEditor;
using UnityEngine;

namespace RegionsAssignation.Editor
{
    internal sealed class RegionsAssignationWindow : EditorWindow
    {
        private const string WindowTitle = "Regions Assignation";
        private const string RulesEditorPrefsKey = "RegionsAssignation_Rules";
        private const float MinWidth = 1200f;
        private const float MinHeight = 740f;
        private const float RulesMinHeightWithoutResults = 340f;
        private const float RulesMaxHeightWithoutResults = 560f;
        private const float RulesMinHeightWithResults = 280f;
        private const float RulesMaxHeightWithResults = 430f;
        private const int MaxMembersPreviewPerRegion = 24;

        [SerializeField] private DefaultAsset targetFolder;
        [SerializeField] private bool includeSubfolders = true;
        [SerializeField] private bool createUnassignedRegion = true;
        [SerializeField] private string unassignedRegionName = "Unassigned";
        [SerializeField] private bool cleanExistingRegionsBeforeProcessing = false;
        [SerializeField] private bool showConfigurationPanel = true;
        [SerializeField] private bool showRulesPanel = true;
        [SerializeField] private bool showAssignmentPanel = true;
        [SerializeField] private bool showContentPanel = true;
        [SerializeField] private bool showOnlyChanged = true;
        [SerializeField] private bool showGeneratedPreview = true;
        [SerializeField] private bool forceApply = false;
        [SerializeField] private List<RegionsAssignationRule> rules = new List<RegionsAssignationRule>();

        private List<RegionsAssignationScriptResult> results = new List<RegionsAssignationScriptResult>();
        private int selectedResultIndex = -1;

        private Vector2 ruleScroll;
        private Vector2 resultScroll;
        private Vector2 regionScroll;
        private Vector2 previewScroll;

        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.None;

        private GUIStyle headerStyle;
        private GUIStyle boxStyle;
        private GUIStyle previewStyle;
        private bool stylesInitialized;

        [MenuItem("Tools/Regions Assignation")]
        private static void ShowWindow()
        {
            var window = GetWindow<RegionsAssignationWindow>(WindowTitle);
            window.minSize = new Vector2(MinWidth, MinHeight);
            window.Show();
        }

        private void OnEnable()
        {
            if (rules == null || rules.Count == 0)
            {
                rules = LoadRules();
            }
        }

        private void OnDisable()
        {
            SaveRules();
        }

        private void InitStyles()
        {
            if (stylesInitialized)
            {
                return;
            }

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(0, 0, 10, 10)
            };

            boxStyle = new GUIStyle("box")
            {
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(0, 0, 5, 5)
            };

            previewStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = false,
                font = Font.CreateDynamicFontFromOSFont("Consolas", 11),
                fontSize = 11
            };

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
            {
                DrawHeader();
                DrawConfiguration();
                DrawRules();
                DrawActions();
                DrawStatus();
                DrawResults();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Regions Assignation Tool", headerStyle);
            EditorGUILayout.HelpBox(
                "Define reglas para mover campos, propiedades, métodos y eventos a #region de forma automática. " +
                "Priority controla qué regla gana cuando varias coinciden (mayor = gana). " +
                "Las flechas ↑/↓ controlan el orden visual de las regiones en el archivo.",
                MessageType.Info);
        }

        private void DrawConfiguration()
        {
            EditorGUILayout.BeginVertical(boxStyle);
            {
                showConfigurationPanel = EditorGUILayout.Foldout(showConfigurationPanel, "Configuración", true);

                if (showConfigurationPanel)
                {
                    EditorGUILayout.Space(3);

                    targetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                        new GUIContent("Target Folder", "Carpeta raíz donde buscar scripts .cs"),
                        targetFolder,
                        typeof(DefaultAsset),
                        false);

                    includeSubfolders = EditorGUILayout.Toggle(
                        new GUIContent("Include Subfolders", "Si está activo, analiza toda la jerarquía"),
                        includeSubfolders);

                    cleanExistingRegionsBeforeProcessing = EditorGUILayout.Toggle(
                        new GUIContent("Clean Existing Regions", "Elimina #region/#endregion del script antes de reclasificar"),
                        cleanExistingRegionsBeforeProcessing);

                    createUnassignedRegion = EditorGUILayout.Toggle(
                        new GUIContent("Create Unassigned Region", "Si está activo, crea región para miembros sin match"),
                        createUnassignedRegion);

                    GUI.enabled = createUnassignedRegion;
                    unassignedRegionName = EditorGUILayout.TextField(
                        new GUIContent("Unassigned Region Name", "Nombre de la región fallback"),
                        unassignedRegionName);
                    GUI.enabled = true;
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawRules()
        {
            EditorGUILayout.BeginVertical(boxStyle);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    showRulesPanel = EditorGUILayout.Foldout(showRulesPanel, $"Reglas ({rules.Count})", true);
                    GUILayout.FlexibleSpace();

                    GUI.enabled = rules.Count > 0;
                    if (GUILayout.Button("Expand All", GUILayout.Width(90)))
                    {
                        for (int index = 0; index < rules.Count; index++)
                        {
                            rules[index].IsExpanded = true;
                        }
                    }

                    if (GUILayout.Button("Collapse All", GUILayout.Width(95)))
                    {
                        for (int index = 0; index < rules.Count; index++)
                        {
                            rules[index].IsExpanded = false;
                        }
                    }
                    GUI.enabled = true;

                    if (GUILayout.Button("Add Rule", GUILayout.Width(90)))
                    {
                        rules.Add(new RegionsAssignationRule
                        {
                            RegionName = "New Region",
                            MemberKinds = RegionsAssignationMemberKind.Any,
                            IsEnabled = true,
                            IsExpanded = true,
                            Priority = 0
                        });
                    }

                    if (GUILayout.Button("Reset Defaults", GUILayout.Width(120)))
                    {
                        if (EditorUtility.DisplayDialog(
                                "Reset Rules",
                                "Se reemplazarán todas las reglas actuales por el preset inicial.\n\n¿Continuar?",
                                "Sí, resetear",
                                "Cancelar"))
                        {
                            rules = CreateDefaultRules();
                            SaveRules();
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (showRulesPanel)
                {
                    EditorGUILayout.Space(3);

                    float rulesHeight = results.Count > 0
                        ? Mathf.Clamp(position.height * 0.40f, RulesMinHeightWithResults, RulesMaxHeightWithResults)
                        : Mathf.Clamp(position.height * 0.52f, RulesMinHeightWithoutResults, RulesMaxHeightWithoutResults);

                    ruleScroll = EditorGUILayout.BeginScrollView(
                        ruleScroll,
                        GUILayout.MinHeight(rulesHeight),
                        GUILayout.MaxHeight(rulesHeight));
                    {
                        int removeIndex = -1;
                        int moveUpIndex = -1;
                        int moveDownIndex = -1;

                        for (int index = 0; index < rules.Count; index++)
                        {
                            DrawRuleItem(index, ref removeIndex, ref moveUpIndex, ref moveDownIndex);

                            if (removeIndex >= 0 || moveUpIndex >= 0 || moveDownIndex >= 0)
                            {
                                break;
                            }
                        }

                        if (removeIndex >= 0)
                        {
                            rules.RemoveAt(removeIndex);
                        }

                        if (moveUpIndex > 0)
                        {
                            SwapRules(moveUpIndex, moveUpIndex - 1);
                        }

                        if (moveDownIndex >= 0 && moveDownIndex < rules.Count - 1)
                        {
                            SwapRules(moveDownIndex, moveDownIndex + 1);
                        }
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawRuleItem(int index, ref int removeIndex, ref int moveUpIndex, ref int moveDownIndex)
        {
            RegionsAssignationRule rule = rules[index];

            EditorGUILayout.BeginVertical("box");
            {
                EditorGUILayout.BeginHorizontal();
                {
                    Rect foldoutRect = GUILayoutUtility.GetRect(14f, EditorGUIUtility.singleLineHeight, GUILayout.Width(14f));
                    rule.IsExpanded = EditorGUI.Foldout(foldoutRect, rule.IsExpanded, GUIContent.none, true);
                    rule.IsEnabled = EditorGUILayout.Toggle(rule.IsEnabled, GUILayout.Width(20));

                    GUI.enabled = index > 0;
                    if (GUILayout.Button("↑", GUILayout.Width(22)))
                    {
                        moveUpIndex = index;
                    }

                    GUI.enabled = index < rules.Count - 1;
                    if (GUILayout.Button("↓", GUILayout.Width(22)))
                    {
                        moveDownIndex = index;
                    }

                    GUI.enabled = true;

                    string summary = GetRuleSummary(rule);
                    EditorGUILayout.LabelField($"Rule {index + 1} · {summary}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Remove", GUILayout.Width(80)))
                    {
                        removeIndex = index;
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (rule.IsExpanded)
                {
                    rule.RegionName = EditorGUILayout.TextField(new GUIContent("Region Name"), rule.RegionName);
                    rule.Priority = Mathf.Clamp(
                        EditorGUILayout.IntField(
                            new GUIContent("Priority", "Controla qué regla gana cuando varias coinciden (mayor = prioridad de match). El orden visual de las regiones en el archivo se controla con las flechas ↑/↓."),
                            rule.Priority),
                        -1000,
                        1000);
                    rule.MemberKinds = (RegionsAssignationMemberKind)EditorGUILayout.EnumFlagsField(
                        new GUIContent("Member Kinds"),
                        rule.MemberKinds);
                    rule.AccessKinds = (RegionsAssignationAccessKind)EditorGUILayout.EnumFlagsField(
                        new GUIContent("Access Kinds", "Filtro por modificador de acceso del miembro"),
                        rule.AccessKinds);
                    rule.ModifierKinds = (RegionsAssignationModifierKind)EditorGUILayout.EnumFlagsField(
                        new GUIContent("Modifier Kinds", "Filtro por modificadores del miembro (static, abstract, virtual, etc.)"),
                        rule.ModifierKinds);

                    EditorGUILayout.BeginHorizontal();
                    {
                        rule.MatchUnityLifecycleMethods = EditorGUILayout.ToggleLeft(
                            "Unity Lifecycle Methods",
                            rule.MatchUnityLifecycleMethods,
                            GUILayout.Width(200));

                        rule.MatchOverrideMethods = EditorGUILayout.ToggleLeft(
                            "Override Methods",
                            rule.MatchOverrideMethods,
                            GUILayout.Width(170));
                    }
                    EditorGUILayout.EndHorizontal();

                    rule.NameStartsWith = EditorGUILayout.TextField(
                        new GUIContent("Name Starts With", "Tokens separados por coma (; o | también)"),
                        rule.NameStartsWith);

                    rule.NameContains = EditorGUILayout.TextField(
                        new GUIContent("Name Contains", "Tokens separados por coma"),
                        rule.NameContains);

                    rule.NameRegex = EditorGUILayout.TextField(
                        new GUIContent("Name Regex", "Regex opcional sobre nombre de miembro"),
                        rule.NameRegex);

                    rule.AttributeContains = EditorGUILayout.TextField(
                        new GUIContent("Attribute Contains", "Tokens para nombre de atributo"),
                        rule.AttributeContains);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();
            {
                GUI.enabled = targetFolder != null;
                if (GUILayout.Button("Analyze + Preview", GUILayout.Height(30)))
                {
                    AnalyzeScripts();
                }

                GUI.enabled = results.Any(result => result.IsSuccess && result.IsSelected && (result.HasChanges || forceApply));
                if (GUILayout.Button("Apply Selected Changes", GUILayout.Height(30)))
                {
                    ApplySelectedChanges();
                }

                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }
        }

        private void DrawResults()
        {
            if (results.Count == 0)
            {
                return;
            }

            int changedCount = results.Count(result => result.IsSuccess && result.HasChanges);
            int selectedCount = results.Count(result => result.IsSuccess && (result.HasChanges || forceApply) && result.IsSelected);
            int invalidCount = results.Count(result => !result.IsSuccess);

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginVertical(boxStyle);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField(
                        $"Resultados: {results.Count} archivos | Cambios: {changedCount} | Seleccionados: {selectedCount} | Inválidos: {invalidCount}",
                        EditorStyles.miniBoldLabel);

                    if (GUILayout.Button(showRulesPanel ? "Hide Rules" : "Show Rules", GUILayout.Width(95)))
                    {
                        showRulesPanel = !showRulesPanel;
                    }

                    showOnlyChanged = EditorGUILayout.ToggleLeft("Only Changed", showOnlyChanged, GUILayout.Width(110));
                    showGeneratedPreview = EditorGUILayout.ToggleLeft("Show Generated", showGeneratedPreview, GUILayout.Width(120));
                    forceApply = EditorGUILayout.ToggleLeft(
                        new GUIContent("Force Apply", "Permite seleccionar y aplicar archivos aunque no se detecten cambios"),
                        forceApply, GUILayout.Width(100));
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(6);

                EditorGUILayout.BeginHorizontal();
                {
                    DrawResultsList();
                    DrawSelectedPreview();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawResultsList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(430));
            {
                EditorGUILayout.LabelField("Archivos", EditorStyles.boldLabel);

                List<int> visibleIndices = GetVisibleResultIndices();
                EnsureSelectedVisible(visibleIndices);

                resultScroll = EditorGUILayout.BeginScrollView(resultScroll, GUILayout.ExpandHeight(true));
                {
                    for (int i = 0; i < visibleIndices.Count; i++)
                    {
                        int index = visibleIndices[i];
                        RegionsAssignationScriptResult result = results[index];

                        EditorGUILayout.BeginHorizontal();
                        {
                            if (result.IsSuccess && (result.HasChanges || forceApply))
                            {
                                result.IsSelected = EditorGUILayout.Toggle(result.IsSelected, GUILayout.Width(18));
                            }
                            else
                            {
                                GUILayout.Space(18);
                            }

                            string icon = GetResultIcon(result);
                            string label = $"{icon} {ToProjectRelativePath(result.FilePath)}";

                            GUIStyle style = selectedResultIndex == index
                                ? EditorStyles.toolbarButton
                                : EditorStyles.miniButton;

                            if (GUILayout.Button(label, style, GUILayout.ExpandWidth(true)))
                            {
                                selectedResultIndex = index;
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedPreview()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            {
                if (selectedResultIndex < 0 || selectedResultIndex >= results.Count)
                {
                    EditorGUILayout.HelpBox("Selecciona un archivo para ver el preview.", MessageType.Info);
                }
                else
                {
                    RegionsAssignationScriptResult result = results[selectedResultIndex];

                    EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(ToProjectRelativePath(result.FilePath), EditorStyles.miniLabel);

                    if (!string.IsNullOrEmpty(result.Message))
                    {
                        MessageType messageType = result.IsSuccess ? MessageType.Info : MessageType.Warning;
                        EditorGUILayout.HelpBox(result.Message, messageType);
                    }

                    if (result.IsSuccess)
                    {
                        EditorGUILayout.LabelField(
                            $"Members: {result.MemberCount} | Assigned: {result.AssignedCount} | Changed: {(result.HasChanges ? "Yes" : "No")}",
                            EditorStyles.miniBoldLabel);

                        EditorGUILayout.Space(4);
                        showAssignmentPanel = EditorGUILayout.Foldout(showAssignmentPanel, "Asignación", true);
                        if (showAssignmentPanel)
                        {
                            regionScroll = EditorGUILayout.BeginScrollView(regionScroll, GUILayout.MinHeight(130), GUILayout.MaxHeight(230));
                            {
                                if (result.RegionGroups.Count == 0)
                                {
                                    EditorGUILayout.HelpBox("No hay grupos para mostrar.", MessageType.Info);
                                }

                                for (int index = 0; index < result.RegionGroups.Count; index++)
                                {
                                    DrawRegionAssignmentGroup(result.RegionGroups[index]);
                                }
                            }
                            EditorGUILayout.EndScrollView();
                        }

                        EditorGUILayout.Space(5);
                        string contentLabel = showGeneratedPreview ? "Contenido Generado" : "Contenido Original";
                        showContentPanel = EditorGUILayout.Foldout(showContentPanel, contentLabel, true);

                        if (showContentPanel)
                        {
                            string preview = showGeneratedPreview
                                ? result.PreviewContent
                                : result.OriginalContent;

                            previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.ExpandHeight(true));
                            {
                                EditorGUILayout.TextArea(preview, previewStyle, GUILayout.ExpandHeight(true));
                            }
                            EditorGUILayout.EndScrollView();
                        }
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void AnalyzeScripts()
        {
            if (targetFolder == null)
            {
                SetStatus("Debes seleccionar una carpeta objetivo.", MessageType.Warning);
                return;
            }

            if (!TryGetAbsoluteTargetFolder(out string absoluteFolder, out string validationError))
            {
                SetStatus(validationError, MessageType.Error);
                return;
            }

            string[] files;
            try
            {
                SearchOption option = includeSubfolders
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                files = Directory.GetFiles(absoluteFolder, "*.cs", option);
            }
            catch (Exception exception)
            {
                SetStatus($"No se pudieron listar los archivos: {exception.Message}", MessageType.Error);
                return;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            results = new List<RegionsAssignationScriptResult>(files.Length);
            for (int index = 0; index < files.Length; index++)
            {
                RegionsAssignationScriptResult result = RegionsAssignationProcessor.ProcessFile(
                    files[index],
                    rules,
                    createUnassignedRegion,
                    unassignedRegionName,
                    cleanExistingRegionsBeforeProcessing);

                results.Add(result);
            }

            selectedResultIndex = FindFirstPreferredResultIndex();

            int changed = results.Count(result => result.IsSuccess && result.HasChanges);
            int invalid = results.Count(result => !result.IsSuccess);
            SetStatus(
                $"Analizados {results.Count} scripts. {changed} con cambios sugeridos. {invalid} omitidos por parseo/validación.",
                MessageType.Info);
        }

        private void ApplySelectedChanges()
        {
            int applied = 0;
            int failed = 0;
            string firstError = string.Empty;

            for (int index = 0; index < results.Count; index++)
            {
                RegionsAssignationScriptResult result = results[index];
                if (!result.IsSuccess || !result.IsSelected || (!result.HasChanges && !forceApply))
                {
                    continue;
                }

                try
                {
                    File.WriteAllText(result.FilePath, result.PreviewContent);
                    applied++;
                }
                catch (Exception exception)
                {
                    failed++;
                    if (firstError.Length == 0)
                    {
                        firstError = $"{ToProjectRelativePath(result.FilePath)}: {exception.Message}";
                    }
                }
            }

            AssetDatabase.Refresh();

            if (applied == 0 && failed == 0)
            {
                SetStatus("No hay cambios seleccionados para aplicar.", MessageType.Warning);
                return;
            }

            if (failed > 0)
            {
                SetStatus($"Aplicados: {applied}. Fallidos: {failed}. Primer error: {firstError}", MessageType.Warning);
            }
            else
            {
                SetStatus($"Aplicados correctamente {applied} archivos.", MessageType.Info);
            }

            AnalyzeScripts();
        }

        private List<int> GetVisibleResultIndices()
        {
            var visible = new List<int>();
            for (int index = 0; index < results.Count; index++)
            {
                if (showOnlyChanged && !(results[index].IsSuccess && (results[index].HasChanges || forceApply)))
                {
                    continue;
                }

                visible.Add(index);
            }

            return visible;
        }

        private void EnsureSelectedVisible(IReadOnlyList<int> visibleIndices)
        {
            if (visibleIndices.Count == 0)
            {
                selectedResultIndex = -1;
                return;
            }

            if (!visibleIndices.Contains(selectedResultIndex))
            {
                selectedResultIndex = visibleIndices[0];
            }
        }

        private int FindFirstPreferredResultIndex()
        {
            for (int index = 0; index < results.Count; index++)
            {
                if (results[index].IsSuccess && results[index].HasChanges)
                {
                    return index;
                }
            }

            for (int index = 0; index < results.Count; index++)
            {
                if (results[index].IsSuccess)
                {
                    return index;
                }
            }

            return results.Count > 0 ? 0 : -1;
        }

        private bool TryGetAbsoluteTargetFolder(out string absoluteFolder, out string error)
        {
            absoluteFolder = string.Empty;
            error = string.Empty;

            string relativePath = AssetDatabase.GetAssetPath(targetFolder);
            if (string.IsNullOrEmpty(relativePath) || !AssetDatabase.IsValidFolder(relativePath))
            {
                error = "El objeto seleccionado no es una carpeta válida de Assets/.";
                return false;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            absoluteFolder = Path.GetFullPath(Path.Combine(projectRoot, relativePath));

            if (!Directory.Exists(absoluteFolder))
            {
                error = "La carpeta objetivo no existe en disco.";
                return false;
            }

            return true;
        }

        private static string ToProjectRelativePath(string absolutePath)
        {
            string normalizedAbsolute = absolutePath.Replace('\\', '/');
            string normalizedDataPath = Application.dataPath.Replace('\\', '/');

            if (normalizedAbsolute.StartsWith(normalizedDataPath, StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + normalizedAbsolute.Substring(normalizedDataPath.Length);
            }

            return normalizedAbsolute;
        }

        private static string GetResultIcon(RegionsAssignationScriptResult result)
        {
            if (!result.IsSuccess)
            {
                return "⚠";
            }

            if (!result.HasChanges)
            {
                return "✓";
            }

            return "●";
        }

        private static string GetRuleSummary(RegionsAssignationRule rule)
        {
            string regionName = string.IsNullOrWhiteSpace(rule.RegionName)
                ? "Sin nombre"
                : rule.RegionName.Trim();

            return $"{regionName} | P:{rule.Priority} | {rule.MemberKinds}";
        }

        private void DrawRegionAssignmentGroup(RegionsAssignationRegionGroup group)
        {
            EditorGUILayout.LabelField($"#region {group.RegionName} ({group.MemberNames.Count})", EditorStyles.boldLabel);

            int membersToShow = Math.Min(group.MemberNames.Count, MaxMembersPreviewPerRegion);
            for (int memberIndex = 0; memberIndex < membersToShow; memberIndex++)
            {
                EditorGUILayout.LabelField($"• {group.MemberNames[memberIndex]}", EditorStyles.label);
            }

            if (group.MemberNames.Count > membersToShow)
            {
                int remaining = group.MemberNames.Count - membersToShow;
                EditorGUILayout.LabelField($"... +{remaining} miembros", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(3);
        }

        private void SwapRules(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= rules.Count || toIndex < 0 || toIndex >= rules.Count)
            {
                return;
            }

            RegionsAssignationRule temp = rules[fromIndex];
            rules[fromIndex] = rules[toIndex];
            rules[toIndex] = temp;
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
        }

        private void SaveRules()
        {
            var wrapper = new RulesWrapper { Rules = rules };
            string json = JsonUtility.ToJson(wrapper, false);
            EditorPrefs.SetString(RulesEditorPrefsKey, json);
        }

        private static List<RegionsAssignationRule> LoadRules()
        {
            if (EditorPrefs.HasKey(RulesEditorPrefsKey))
            {
                string json = EditorPrefs.GetString(RulesEditorPrefsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var wrapper = JsonUtility.FromJson<RulesWrapper>(json);
                    if (wrapper != null && wrapper.Rules != null && wrapper.Rules.Count > 0)
                    {
                        return wrapper.Rules;
                    }
                }
            }

            return CreateDefaultRules();
        }

        [Serializable]
        private sealed class RulesWrapper
        {
            public List<RegionsAssignationRule> Rules = new List<RegionsAssignationRule>();
        }

        private static List<RegionsAssignationRule> CreateDefaultRules()
        {
            return new List<RegionsAssignationRule>
            {
                new RegionsAssignationRule
                {
                    IsEnabled = true,
                    RegionName = "Main Events",
                    Priority = 100,
                    MemberKinds = RegionsAssignationMemberKind.Method,
                    AccessKinds = RegionsAssignationAccessKind.Any,
                    ModifierKinds = RegionsAssignationModifierKind.Any,
                    MatchUnityLifecycleMethods = true,
                    MatchOverrideMethods = true,
                    NameStartsWith = string.Empty,
                    NameContains = string.Empty,
                    NameRegex = string.Empty,
                    AttributeContains = string.Empty
                },
                new RegionsAssignationRule
                {
                    IsEnabled = true,
                    RegionName = "Main Events",
                    Priority = 90,
                    MemberKinds = RegionsAssignationMemberKind.Constructor,
                    AccessKinds = RegionsAssignationAccessKind.Any,
                    ModifierKinds = RegionsAssignationModifierKind.Any,
                    MatchUnityLifecycleMethods = false,
                    MatchOverrideMethods = false,
                    NameStartsWith = string.Empty,
                    NameContains = string.Empty,
                    NameRegex = string.Empty,
                    AttributeContains = string.Empty
                },
                new RegionsAssignationRule
                {
                    IsEnabled = true,
                    RegionName = "Events",
                    Priority = 20,
                    MemberKinds = RegionsAssignationMemberKind.Method,
                    AccessKinds = RegionsAssignationAccessKind.Any,
                    ModifierKinds = RegionsAssignationModifierKind.Any,
                    MatchUnityLifecycleMethods = false,
                    MatchOverrideMethods = false,
                    NameStartsWith = "On",
                    NameContains = string.Empty,
                    NameRegex = string.Empty,
                    AttributeContains = string.Empty
                },
                new RegionsAssignationRule
                {
                    IsEnabled = true,
                    RegionName = "Properties",
                    Priority = 0,
                    MemberKinds = RegionsAssignationMemberKind.Property,
                    AccessKinds = RegionsAssignationAccessKind.Any,
                    ModifierKinds = RegionsAssignationModifierKind.Any,
                    MatchUnityLifecycleMethods = false,
                    MatchOverrideMethods = false,
                    NameStartsWith = string.Empty,
                    NameContains = string.Empty,
                    NameRegex = string.Empty,
                    AttributeContains = string.Empty
                },
                new RegionsAssignationRule
                {
                    IsEnabled = true,
                    RegionName = "Events Fields",
                    Priority = -5,
                    MemberKinds = RegionsAssignationMemberKind.Event,
                    AccessKinds = RegionsAssignationAccessKind.Any,
                    ModifierKinds = RegionsAssignationModifierKind.Any,
                    MatchUnityLifecycleMethods = false,
                    MatchOverrideMethods = false,
                    NameStartsWith = string.Empty,
                    NameContains = string.Empty,
                    NameRegex = string.Empty,
                    AttributeContains = string.Empty
                },
                new RegionsAssignationRule
                {
                    IsEnabled = true,
                    RegionName = "Fields",
                    Priority = -10,
                    MemberKinds = RegionsAssignationMemberKind.Field,
                    AccessKinds = RegionsAssignationAccessKind.Any,
                    ModifierKinds = RegionsAssignationModifierKind.Any,
                    MatchUnityLifecycleMethods = false,
                    MatchOverrideMethods = false,
                    NameStartsWith = string.Empty,
                    NameContains = string.Empty,
                    NameRegex = string.Empty,
                    AttributeContains = string.Empty
                },
                new RegionsAssignationRule
                {
                    IsEnabled = true,
                    RegionName = "Methods",
                    Priority = -20,
                    MemberKinds = RegionsAssignationMemberKind.Method,
                    AccessKinds = RegionsAssignationAccessKind.Any,
                    ModifierKinds = RegionsAssignationModifierKind.Any,
                    MatchUnityLifecycleMethods = false,
                    MatchOverrideMethods = false,
                    NameStartsWith = string.Empty,
                    NameContains = string.Empty,
                    NameRegex = string.Empty,
                    AttributeContains = string.Empty
                },
                new RegionsAssignationRule
                {
                    IsEnabled = true,
                    RegionName = "Nested Types",
                    Priority = -30,
                    MemberKinds = RegionsAssignationMemberKind.NestedType,
                    AccessKinds = RegionsAssignationAccessKind.Any,
                    ModifierKinds = RegionsAssignationModifierKind.Any,
                    MatchUnityLifecycleMethods = false,
                    MatchOverrideMethods = false,
                    NameStartsWith = string.Empty,
                    NameContains = string.Empty,
                    NameRegex = string.Empty,
                    AttributeContains = string.Empty
                }
            };
        }
    }
}
