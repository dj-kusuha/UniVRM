﻿using System;
using System.Collections.Generic;
using UniGLTF.AltTask;
using UnityEngine;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UniGLTF
{
    [Flags]
    public enum TextureLoadFlags
    {
        None = 0,
        Used = 1,
        External = 1 << 1,
    }

    public struct TextureLoadInfo
    {
        public readonly Texture2D Texture;
        public readonly TextureLoadFlags Flags;
        public bool IsUsed => Flags.HasFlag(TextureLoadFlags.Used);
        public bool IsExternal => Flags.HasFlag(TextureLoadFlags.External);

        public TextureLoadInfo(Texture2D texture, bool used, bool isExternal)
        {
            Texture = texture;
            var flags = TextureLoadFlags.None;
            if (used)
            {
                flags |= TextureLoadFlags.Used;
            }
            if (isExternal)
            {
                flags |= TextureLoadFlags.External;
            }
            Flags = flags;
        }
    }

    public delegate Awaitable<Texture2D> GetTextureAsyncFunc(glTF gltf, GetTextureParam param);
    public class TextureFactory : IDisposable
    {
        glTF m_gltf;
        IStorage m_storage;
        Dictionary<string, Texture2D> m_externalMap;
        public bool TryGetExternal(GetTextureParam param, bool used, out Texture2D external)
        {
            if (param.Index0.HasValue && m_externalMap != null)
            {
                var gltfTexture = m_gltf.textures[param.Index0.Value];
                m_gltf.GetImageBytes(m_storage, gltfTexture.source, out string textureName);

                if (m_externalMap.TryGetValue(textureName, out external))
                {
                    Debug.Log($"use external: {textureName}");
                    m_textureCache.Add(param, new TextureLoadInfo(external, used, true));
                    return external;
                }
            }
            external = default;
            return false;
        }

        public UnityPath ImageBaseDir { get; set; }

        public TextureFactory(glTF gltf, IStorage storage,
        IEnumerable<(string, UnityEngine.Object)> externalMap)
        {
            m_gltf = gltf;
            m_storage = storage;
            if (externalMap != null)
            {
                m_externalMap = externalMap
                    .Select(kv => (kv.Item1, kv.Item2 as Texture2D))
                    .Where(kv => kv.Item2 != null)
                    .ToDictionary(kv => kv.Item1, kv => kv.Item2);
            }
        }

        public void Dispose()
        {
            foreach (var x in ObjectsForSubAsset())
            {
                UnityEngine.Object.DestroyImmediate(x, true);
            }
        }

        public IEnumerable<UnityEngine.Object> ObjectsForSubAsset()
        {
            foreach (var kv in m_textureCache)
            {
                yield return kv.Value.Texture;
            }
        }

        Dictionary<GetTextureParam, TextureLoadInfo> m_textureCache = new Dictionary<GetTextureParam, TextureLoadInfo>();

        public IEnumerable<TextureLoadInfo> Textures => m_textureCache.Values;

        public virtual async Awaitable<TextureLoadInfo> LoadTextureAsync(int index, bool used)
        {
#if UNIGLTF_USE_WEBREQUEST_TEXTURELOADER
            return UnityWebRequestTextureLoader.LoadTextureAsync(index);
#else
            var texture = await GltfTextureLoader.LoadTextureAsync(m_gltf, m_storage, index);
            return new TextureLoadInfo(texture, used, false);
#endif
        }

        async Awaitable<Texture2D> GetOrCreateBaseTexture(glTF gltf, int index, bool used)
        {
            var defaultParam = GetTextureParam.Create(gltf, index);
            if (!m_textureCache.TryGetValue(defaultParam, out TextureLoadInfo cacheInfo))
            {
                cacheInfo = await LoadTextureAsync(index, used);
                m_textureCache.Add(defaultParam, cacheInfo);
            }
            return cacheInfo.Texture;
        }

        /// <summary>
        /// テクスチャーをロード、必要であれば変換して返す。
        /// 同じものはキャッシュを返す
        /// </summary>
        /// <param name="texture_type">変換の有無を判断する: METALLIC_GLOSS_PROP</param>
        /// <param name="roughnessFactor">METALLIC_GLOSS_PROPの追加パラメーター</param>
        /// <param name="indices">gltf の texture index</param>
        /// <returns></returns>
        public async Awaitable<Texture2D> GetTextureAsync(glTF gltf, GetTextureParam param)
        {
            if (m_textureCache.TryGetValue(param, out TextureLoadInfo cacheInfo))
            {
                return cacheInfo.Texture;
            }
            if (TryGetExternal(param, true, out Texture2D external))
            {
                return external;
            }

            switch (param.TextureType)
            {
                case GetTextureParam.NORMAL_PROP:
                    {
                        if (Application.isPlaying)
                        {
                            var baseTexture = await GetOrCreateBaseTexture(gltf, param.Index0.Value, false);
                            var converted = new NormalConverter().GetImportTexture(baseTexture);
                            var info = new TextureLoadInfo(converted, true, false);
                            m_textureCache.Add(param, info);
                            return info.Texture;
                        }
                        else
                        {
#if UNITY_EDITOR
                            var info = await LoadTextureAsync(param.Index0.Value, true);
                            m_textureCache.Add(GetTextureParam.CreateNormal(gltf, param.Index0.Value), info);

                            var textureAssetPath = AssetDatabase.GetAssetPath(info.Texture);
                            TextureIO.MarkTextureAssetAsNormalMap(textureAssetPath);
#endif
                            return info.Texture;
                        }
                    }

                case GetTextureParam.METALLIC_GLOSS_PROP:
                    {
                        // Bake roughnessFactor values into a texture.
                        var baseTexture = await GetOrCreateBaseTexture(gltf, param.Index0.Value, false);
                        var converted = new MetallicRoughnessConverter(param.MetallicFactor).GetImportTexture(baseTexture);
                        var info = new TextureLoadInfo(converted, true, false);
                        m_textureCache.Add(param, info);
                        return info.Texture;
                    }

                case GetTextureParam.OCCLUSION_PROP:
                    {
                        var baseTexture = await GetOrCreateBaseTexture(gltf, param.Index0.Value, false);
                        var converted = new OcclusionConverter().GetImportTexture(baseTexture);
                        var info = new TextureLoadInfo(converted, true, false);
                        m_textureCache.Add(param, info);
                        return info.Texture;
                    }

                default:
                    {
                        var baseTexture = await GetOrCreateBaseTexture(gltf, param.Index0.Value, true);
                        return baseTexture;
                    }

                    throw new NotImplementedException();
            }
        }
    }
}
