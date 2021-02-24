﻿using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.AssetImporters;
using UnityEngine;

namespace UniGLTF
{
    [CustomEditor(typeof(GltfScriptedImporter))]
    public class GltfScriptedImporterEditorGUI : ScriptedImporterEditor
    {
        GltfScriptedImporter m_importer;
        GltfParser m_parser;

        public override void OnEnable()
        {
            m_importer = target as GltfScriptedImporter;
            m_parser = new GltfParser();
            m_parser.ParsePath(m_importer.assetPath);
        }

        enum Tabs
        {
            Model,
            Materials,
        }
        static Tabs s_currentTab;

        public override void OnInspectorGUI()
        {
            s_currentTab = MeshUtility.TabBar.OnGUI(s_currentTab);
            switch (s_currentTab)
            {
                case Tabs.Model:
                    base.OnInspectorGUI();
                    break;

                case Tabs.Materials:
                    OnGUIMaterial(m_importer, m_parser);
                    break;
            }
        }

        static bool s_foldMaterials = true;
        static bool s_foldTextures = true;

        static void OnGUIMaterial(GltfScriptedImporter importer, GltfParser parser)
        {
            if (GUILayout.Button("Extract Materials And Textures ..."))
            {
                importer.ExtractMaterialsAndTextures();
            }

            // ObjectMap
            s_foldMaterials = EditorGUILayout.Foldout(s_foldMaterials, "Remapped Materials");
            if (s_foldMaterials)
            {
                DrawRemapGUI<UnityEngine.Material>(importer, parser.GLTF.materials.Select(x => x.name));
            }

            s_foldTextures = EditorGUILayout.Foldout(s_foldMaterials, "Remapped Textures");
            if (s_foldTextures)
            {
                DrawRemapGUI<UnityEngine.Texture2D>(importer, parser.EnumerateTextures().Select(x => x.Name));
            }

            if (GUILayout.Button("Clear"))
            {
                importer.ClearExternalObjects<UnityEngine.Material>();
                importer.ClearExternalObjects<UnityEngine.Texture2D>();
            }
        }

        static void DrawRemapGUI<T>(GltfScriptedImporter importer, IEnumerable<string> names) where T : UnityEngine.Object
        {
            EditorGUI.indentLevel++;
            var map = importer.GetExternalObjectMap()
                .Select(x => (x.Key.name, x.Value as T))
                .Where(x => x.Item2 != null)
                .ToDictionary(x => x.Item1, x => x.Item2)
                ;
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name))
                {
                    throw new System.ArgumentNullException();
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(name);
                map.TryGetValue(name, out T value);
                if (map.Any() && value == null)
                {
                    var a = 0;
                    // Debug.Log($"{name}: {value}");
                }
                var asset = EditorGUILayout.ObjectField(value, typeof(T), true) as T;
                if (asset != value)
                {
                    importer.SetExternalUnityObject(new AssetImporter.SourceAssetIdentifier(value), asset);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }
    }
}
