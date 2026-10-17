using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Kingsfall.Chess;

namespace Kingsfall.EditorTools
{
    /// <summary>Generates every asset, the scene, and the Linux player. Run from the menu or batch mode.</summary>
    public static class KingsfallBuilder
    {
        const string Root = "Assets/Kingsfall/";
        const string Gen = Root + "Generated/";
        const string ScenePath = Root + "Scenes/Kingsfall.unity";
        const string BuildPath = "Build/Linux/JoshsChessTrainer.x86_64";
        const float FloorY = -0.9f;

        static Shader stoneShader, glowShader;

        [MenuItem("Kingsfall/Generate Everything")]
        public static void GenerateAll()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ConfigureProject();
            Directory.CreateDirectory(Gen);
            Directory.CreateDirectory(Root + "Scenes");
            Directory.CreateDirectory(Root + "Settings");
            AssetDatabase.Refresh();
            stoneShader = Shader.Find("Kingsfall/Stone");
            glowShader = Shader.Find("Kingsfall/Glow");
            if (!stoneShader || !glowShader) throw new Exception("Kingsfall shaders not found");

            var lib = LoadOrCreate<KingsfallLibrary>(Root + "Resources/KingsfallLibrary.asset");
            var tex = MakeTextures();
            MakeMaterials(lib, tex);
            MakePieceMeshes(lib);
            MakeUtilityMeshes(lib);
            lib.titleFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "Resources/TitleFont.ttf");
            lib.headFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "Resources/HeadFont.ttf");
            lib.bodyFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "Resources/BodyFont.ttf");
            lib.symbolFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "Resources/SymbolFont.ttf");
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();

            var urp = MakePipeline();
            var profile = MakeVolumeProfile();
            BuildScene(lib, tex, profile);
            AssetDatabase.SaveAssets();
            Debug.Log($"KINGSFALL_GENERATE_OK in {sw.Elapsed.TotalSeconds:F1}s");
        }

        [MenuItem("Kingsfall/Build Linux Player")]
        public static void BuildLinux()
        {
            ConfigureProject();
            Directory.CreateDirectory("Build/Linux");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Build failed: " + report.summary.result);
            Debug.Log($"KINGSFALL_BUILD_OK size={report.summary.totalSize / (1024 * 1024)}MB");
        }

        public static void GenerateAndBuild()
        {
            GenerateAll();
            BuildLinux();
        }

        // ------------------------------------------------------------ project settings

        static void ConfigureProject()
        {
            PlayerSettings.companyName = "Host Hobbit";
            PlayerSettings.productName = "Josh's Chess Trainer";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLCore });
            try { PlayerSettings.SplashScreen.show = false; } catch { }

            var ps = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            var so = new SerializedObject(ps);
            var input = so.FindProperty("activeInputHandler");
            if (input != null) { input.intValue = 0; so.ApplyModifiedPropertiesWithoutUndo(); }
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static void SaveAsset(UnityEngine.Object obj, string path)
        {
            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(obj, path);
        }

        // ------------------------------------------------------------ textures

        class Tex
        {
            public Texture2D Soft, SoftAlpha, Fire, Ring, Dot, CaptureRing, SelectRing, Square, Smoke, Beam, Bolt, Rune, God;
        }

        static Tex MakeTextures()
        {
            var t = new Tex();
            t.Soft = SaveTex("soft", 128, (u, v) => { float d = R(u, v); float a = Mathf.Exp(-d * d * 9f); return Pre(a); });
            t.SoftAlpha = SaveTex("soft_alpha", 128, (u, v) => { float d = R(u, v); return new Color(1, 1, 1, Mathf.Exp(-d * d * 7f) * Mathf.Clamp01(1 - d)); });
            t.Fire = SaveTex("fire", 128, (u, v) =>
            {
                float d = R(u, v);
                float n = Mathf.PerlinNoise(u * 6f, v * 6f) * 0.6f + Mathf.PerlinNoise(u * 13f + 5, v * 13f) * 0.4f;
                float a = Mathf.Clamp01(Mathf.Exp(-d * d * 6f) * (0.6f + 0.8f * n) - 0.08f);
                return Pre(a);
            });
            t.Ring = SaveTex("ring", 256, (u, v) => { float d = R(u, v); float a = Mathf.Exp(-Mathf.Pow((d - 0.82f) / 0.07f, 2)) + 0.25f * Mathf.Exp(-Mathf.Pow((d - 0.68f) / 0.14f, 2)); return Pre(Mathf.Clamp01(a)); });
            t.Dot = SaveTex("dot", 128, (u, v) =>
            {
                float d = R(u, v);
                float core = Mathf.Clamp01((0.5f - d) / 0.08f);
                float halo = Mathf.Exp(-d * d * 6f) * 0.55f;
                return Pre(Mathf.Clamp01(core * 0.9f + halo));
            });
            t.CaptureRing = SaveTex("capture_ring", 256, (u, v) =>
            {
                float d = R(u, v);
                float ang = Mathf.Atan2(v - 0.5f, u - 0.5f);
                float ring = Mathf.Exp(-Mathf.Pow((d - 0.86f) / 0.045f, 2));
                float notches = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 2f)), 24f) * Mathf.Exp(-Mathf.Pow((d - 0.7f) / 0.1f, 2));
                float fill = Mathf.Clamp01(1f - d) * 0.18f;
                return Pre(Mathf.Clamp01(ring + notches * 0.9f + fill));
            });
            t.SelectRing = SaveTex("select_ring", 256, (u, v) =>
            {
                float d = R(u, v);
                float a = Mathf.Exp(-Mathf.Pow((d - 0.88f) / 0.035f, 2)) + 0.6f * Mathf.Exp(-Mathf.Pow((d - 0.76f) / 0.02f, 2)) + Mathf.Clamp01(1f - d) * 0.15f;
                return Pre(Mathf.Clamp01(a));
            });
            t.Square = SaveTex("square", 128, (u, v) =>
            {
                float e = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
                float a = Mathf.Exp(-e * 22f) * 0.9f + 0.12f;
                return Pre(Mathf.Clamp01(a));
            });
            t.Smoke = SaveTex("smoke", 128, (u, v) =>
            {
                float d = R(u, v);
                float n = Mathf.PerlinNoise(u * 5f, v * 5f) * 0.7f + Mathf.PerlinNoise(u * 11f + 3, v * 11f) * 0.3f;
                return new Color(1, 1, 1, Mathf.Clamp01((1f - d) * 1.3f * n - 0.1f));
            });
            t.Beam = SaveTex("beam", 64, (u, v) => Pre(Mathf.Clamp01(Mathf.Sin(v * Mathf.PI) * 1.2f)), TextureWrapMode.Repeat);
            t.Bolt = SaveTex("bolt", 64, (u, v) => { float a = Mathf.Exp(-Mathf.Pow((v - 0.5f) / 0.12f, 2)); return new Color(a + 0.3f * a * a, a + 0.3f * a * a, a + 0.3f * a * a, a); });
            t.God = SaveTex("godray", 128, (u, v) =>
            {
                float streak = 0.6f + 0.4f * Mathf.PerlinNoise(u * 24f, 0.5f);
                float along = Mathf.SmoothStep(0f, 0.25f, v) * Mathf.SmoothStep(1f, 0.6f, v);
                return Pre(Mathf.Clamp01(streak * along));
            }, TextureWrapMode.Repeat);
            t.Rune = MakeRune();
            AssetDatabase.Refresh();
            return t;
        }

        static float R(float u, float v) => new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
        static Color Pre(float a) => new Color(a, a, a, a);

        static Texture2D SaveTex(string name, int size, Func<float, float, Color> f, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = f((x + 0.5f) / size, (y + 0.5f) / size);
            t.SetPixels(px);
            t.Apply();
            string path = Gen + name + ".png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = false;
            imp.alphaIsTransparency = true;
            imp.wrapMode = wrap;
            imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D MakeRune()
        {
            const int size = 1024;
            var px = new float[size * size];
            var rng = new System.Random(77);
            void Ring(float radius, float width, float amp)
            {
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x - size / 2f, y - size / 2f).magnitude / (size / 2f);
                    px[y * size + x] += amp * Mathf.Exp(-Mathf.Pow((d - radius) / width, 2));
                }
            }
            Ring(0.97f, 0.006f, 1f);
            Ring(0.93f, 0.003f, 0.7f);
            Ring(0.78f, 0.004f, 0.8f);
            Ring(0.74f, 0.002f, 0.5f);
            Ring(0.6f, 0.003f, 0.35f);
            // radial ticks and glyphs between 0.78 and 0.93
            void Line(Vector2 a, Vector2 b, float w, float amp)
            {
                int steps = (int)((b - a).magnitude * size) + 1;
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                    int cx = (int)((p.x * 0.5f + 0.5f) * size), cy = (int)((p.y * 0.5f + 0.5f) * size);
                    int r = Mathf.CeilToInt(w * size);
                    for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int xx = cx + dx, yy = cy + dy;
                        if (xx < 0 || yy < 0 || xx >= size || yy >= size) continue;
                        float dd = Mathf.Sqrt(dx * dx + dy * dy) / (w * size);
                        px[yy * size + xx] = Mathf.Max(px[yy * size + xx], amp * Mathf.Clamp01(1.2f - dd));
                    }
                }
            }
            int glyphs = 48;
            for (int g = 0; g < glyphs; g++)
            {
                float a0 = g / (float)glyphs * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), tan = new Vector2(-dir.y, dir.x);
                Vector2 c = dir * 0.855f;
                for (int stroke = 0; stroke < 3; stroke++)
                {
                    Vector2 a = c + dir * ((float)rng.NextDouble() - 0.5f) * 0.1f + tan * ((float)rng.NextDouble() - 0.5f) * 0.07f;
                    Vector2 b = c + dir * ((float)rng.NextDouble() - 0.5f) * 0.1f + tan * ((float)rng.NextDouble() - 0.5f) * 0.07f;
                    Line(a, b, 0.0025f, 0.9f);
                }
                Line(dir * 0.74f, dir * 0.78f, 0.0015f, 0.6f);
            }
            for (int g = 0; g < 8; g++)
            {
                float a0 = g / 8f * Mathf.PI * 2f + Mathf.PI / 8f;
                Vector2 dir = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Line(dir * 0.6f, dir * 0.74f, 0.002f, 0.5f);
            }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            var cols = new Color[size * size];
            for (int i = 0; i < cols.Length; i++) { float a = Mathf.Clamp01(px[i]); cols[i] = new Color(a, a, a, a); }
            t.SetPixels(cols);
            t.Apply();
            string path = Gen + "rune.png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = false; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------ materials

        static Material StoneMat(string name, Action<Material> setup)
        {
            var m = new Material(stoneShader) { name = name };
            setup(m);
            m.enableInstancing = false;
            SaveAsset(m, Gen + name + ".mat");
            return m;
        }

        static Material GlowMat(string name, Texture2D tex, Color colour, bool additive = true, float fresnel = 0f, float soft = 0f, CullMode cull = CullMode.Off)
        {
            var m = new Material(glowShader) { name = name };
            m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", colour);
            m.SetFloat("_Fresnel", fresnel);
            m.SetFloat("_SoftEdge", soft);
            m.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Cull", (float)cull);
            m.renderQueue = 3000;
            SaveAsset(m, Gen + name + ".mat");
            return m;
        }

        static readonly Dictionary<string, Material> envMats = new Dictionary<string, Material>();

        static void MakeMaterials(KingsfallLibrary lib, Tex t)
        {
            lib.whitePiece = StoneMat("WhiteMarble", m =>
            {
                m.SetColor("_BaseColor", new Color(0.92f, 0.9f, 0.85f));
                m.SetColor("_BaseColor2", new Color(0.84f, 0.81f, 0.75f));
                m.SetColor("_VeinColor", new Color(0.5f, 0.47f, 0.44f));
                m.SetFloat("_VeinScale", 3.2f); m.SetFloat("_VeinSharpness", 11f); m.SetFloat("_VeinWarp", 1.5f); m.SetFloat("_VeinAmount", 0.75f);
                m.SetFloat("_Smoothness", 0.9f); m.SetFloat("_VeinSmoothness", 0.75f);
                m.SetColor("_GoldColor", new Color(1f, 0.76f, 0.36f)); m.SetFloat("_GoldSmoothness", 0.9f);
                m.SetColor("_InteriorColor", new Color(2.2f, 1.1f, 0.35f));
                m.SetColor("_FlashColor", new Color(2.2f, 1.9f, 1.5f));
                m.SetColor("_DissolveEdge", new Color(4f, 3.2f, 1.4f));
                m.SetFloat("_UseVertexColor", 1f);
            });
            lib.sageRobe = StoneMat("SageRobe", m =>
            {
                // deep royal-blue lapis with gold flecks for the grandmaster's robe and hat
                m.SetColor("_BaseColor", new Color(0.05f, 0.09f, 0.32f));
                m.SetColor("_BaseColor2", new Color(0.08f, 0.05f, 0.26f));
                m.SetColor("_VeinColor", new Color(0.75f, 0.6f, 0.3f));
                m.SetColor("_VeinEmission", new Color(0.25f, 0.35f, 0.9f));
                m.SetFloat("_EmissionPulse", 1f);
                m.SetFloat("_VeinScale", 4.5f); m.SetFloat("_VeinSharpness", 18f); m.SetFloat("_VeinWarp", 1.6f); m.SetFloat("_VeinAmount", 0.6f);
                m.SetFloat("_Smoothness", 0.7f); m.SetFloat("_VeinSmoothness", 0.8f);
                m.SetColor("_GoldColor", new Color(1f, 0.76f, 0.36f)); m.SetFloat("_GoldSmoothness", 0.9f);
                m.SetColor("_InteriorColor", new Color(0.6f, 0.9f, 2.6f));
                m.SetColor("_FlashColor", new Color(1.6f, 1.8f, 2.4f));
                m.SetColor("_DissolveEdge", new Color(1.4f, 2.2f, 4f));
                m.SetFloat("_UseVertexColor", 1f);
            });
            lib.blackPiece = StoneMat("Obsidian", m =>
            {
                m.SetColor("_BaseColor", new Color(0.022f, 0.02f, 0.026f));
                m.SetColor("_BaseColor2", new Color(0.055f, 0.04f, 0.045f));
                m.SetColor("_VeinColor", new Color(0.45f, 0.1f, 0.02f));
                m.SetColor("_VeinEmission", new Color(2.4f, 0.42f, 0.06f));
                m.SetFloat("_EmissionPulse", 1f);
                m.SetFloat("_VeinScale", 3.6f); m.SetFloat("_VeinSharpness", 16f); m.SetFloat("_VeinWarp", 1.8f); m.SetFloat("_VeinAmount", 0.85f);
                m.SetFloat("_Smoothness", 0.94f); m.SetFloat("_VeinSmoothness", 0.5f);
                m.SetColor("_GoldColor", new Color(0.86f, 0.5f, 0.24f)); m.SetFloat("_GoldSmoothness", 0.85f);
                m.SetColor("_InteriorColor", new Color(2.6f, 0.42f, 0.04f));
                m.SetColor("_FlashColor", new Color(2.4f, 1.3f, 0.8f));
                m.SetColor("_DissolveEdge", new Color(5f, 1.5f, 0.3f));
                m.SetFloat("_UseVertexColor", 1f);
            });
            envMats["TileLight"] = StoneMat("TileLight", m =>
            {
                m.SetColor("_BaseColor", new Color(0.86f, 0.82f, 0.75f));
                m.SetColor("_BaseColor2", new Color(0.76f, 0.72f, 0.65f));
                m.SetColor("_VeinColor", new Color(0.52f, 0.46f, 0.4f));
                m.SetFloat("_VeinScale", 0.9f); m.SetFloat("_VeinSharpness", 9f); m.SetFloat("_VeinWarp", 1.6f); m.SetFloat("_VeinAmount", 0.8f);
                m.SetFloat("_Smoothness", 0.93f); m.SetFloat("_VeinSmoothness", 0.85f);
                m.SetFloat("_UseWorldSpace", 1f);
            });
            envMats["TileDark"] = StoneMat("TileDark", m =>
            {
                m.SetColor("_BaseColor", new Color(0.035f, 0.045f, 0.05f));
                m.SetColor("_BaseColor2", new Color(0.07f, 0.075f, 0.08f));
                m.SetColor("_VeinColor", new Color(0.55f, 0.52f, 0.48f));
                m.SetFloat("_VeinScale", 0.7f); m.SetFloat("_VeinSharpness", 22f); m.SetFloat("_VeinWarp", 1.6f); m.SetFloat("_VeinAmount", 0.45f);
                m.SetFloat("_Smoothness", 0.95f); m.SetFloat("_VeinSmoothness", 0.85f);
                m.SetFloat("_UseWorldSpace", 1f);
                m.SetVector("_NoiseOffset", new Vector4(31, 7, 13, 0));
            });
            envMats["Frame"] = StoneMat("FrameWood", m =>
            {
                m.SetColor("_BaseColor", new Color(0.06f, 0.03f, 0.018f));
                m.SetColor("_BaseColor2", new Color(0.11f, 0.055f, 0.028f));
                m.SetColor("_VeinColor", new Color(0.025f, 0.012f, 0.008f));
                m.SetFloat("_VeinScale", 2.2f); m.SetFloat("_VeinSharpness", 3f); m.SetFloat("_VeinWarp", 0.45f); m.SetFloat("_VeinAmount", 0.9f);
                m.SetFloat("_Smoothness", 0.8f); m.SetFloat("_VeinSmoothness", 0.6f);
                m.SetFloat("_UseWorldSpace", 1f);
            });
            envMats["Floor"] = StoneMat("FloorStone", m =>
            {
                m.SetColor("_BaseColor", new Color(0.12f, 0.115f, 0.11f));
                m.SetColor("_BaseColor2", new Color(0.075f, 0.07f, 0.068f));
                m.SetColor("_VeinColor", new Color(0.2f, 0.19f, 0.17f));
                m.SetFloat("_VeinScale", 0.35f); m.SetFloat("_VeinSharpness", 6f); m.SetFloat("_VeinWarp", 1.2f); m.SetFloat("_VeinAmount", 0.5f);
                m.SetFloat("_Smoothness", 0.62f); m.SetFloat("_VeinSmoothness", 0.4f);
                m.SetFloat("_UseWorldSpace", 1f);
                m.SetFloat("_TileSize", 2.2f); m.SetFloat("_GroutWidth", 0.025f);
                m.SetColor("_GroutColor", new Color(0.02f, 0.02f, 0.02f));
                m.SetFloat("_Bumpiness", 0.6f);
            });
            envMats["Pillar"] = StoneMat("PillarStone", m =>
            {
                m.SetColor("_BaseColor", new Color(0.2f, 0.19f, 0.18f));
                m.SetColor("_BaseColor2", new Color(0.13f, 0.125f, 0.12f));
                m.SetColor("_VeinColor", new Color(0.3f, 0.28f, 0.25f));
                m.SetFloat("_VeinScale", 0.5f); m.SetFloat("_VeinSharpness", 5f); m.SetFloat("_VeinWarp", 1.4f); m.SetFloat("_VeinAmount", 0.5f);
                m.SetFloat("_Smoothness", 0.35f); m.SetFloat("_VeinSmoothness", 0.25f);
                m.SetFloat("_UseWorldSpace", 1f);
                m.SetFloat("_Bumpiness", 1.2f);
            });
            envMats["Bronze"] = StoneMat("Bronze", m =>
            {
                m.SetColor("_BaseColor", new Color(0.32f, 0.2f, 0.1f));
                m.SetColor("_BaseColor2", new Color(0.2f, 0.12f, 0.06f));
                m.SetColor("_VeinColor", new Color(0.08f, 0.12f, 0.09f));
                m.SetFloat("_VeinScale", 2f); m.SetFloat("_VeinSharpness", 3f); m.SetFloat("_VeinAmount", 0.4f);
                m.SetFloat("_Metallic", 0.9f);
                m.SetFloat("_Smoothness", 0.62f); m.SetFloat("_VeinSmoothness", 0.3f);
                m.SetColor("_GoldColor", new Color(1f, 0.72f, 0.35f));
                m.SetColor("_InteriorColor", new Color(3.5f, 0.8f, 0.1f));
                m.SetFloat("_UseVertexColor", 1f);
            });
            var gold = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Gold" };
            gold.SetColor("_BaseColor", new Color(1f, 0.74f, 0.34f));
            gold.SetFloat("_Metallic", 1f);
            gold.SetFloat("_Smoothness", 0.86f);
            SaveAsset(gold, Gen + "Gold.mat");
            envMats["Gold"] = gold;

            lib.marker = GlowMat("MarkerMove", t.Dot, new Color(1.6f, 1.15f, 0.45f));
            lib.captureMarker = GlowMat("MarkerCapture", t.CaptureRing, new Color(3.2f, 0.45f, 0.18f));
            lib.selectMarker = GlowMat("MarkerSelect", t.SelectRing, new Color(2.6f, 2f, 0.85f));
            lib.lastMove = GlowMat("MarkerLast", t.Square, new Color(0.55f, 0.42f, 0.18f));
            lib.fire = GlowMat("Fire", t.Fire, new Color(3.2f, 1.6f, 0.6f));
            lib.ember = GlowMat("Ember", t.Soft, new Color(2.5f, 2.2f, 2f));
            lib.dust = GlowMat("Dust", t.SoftAlpha, new Color(0.45f, 0.4f, 0.36f, 0.55f), false);
            lib.spark = GlowMat("Spark", t.Soft, new Color(3f, 3f, 3f));
            lib.lightning = GlowMat("Lightning", t.Bolt, new Color(2f, 2f, 2f));
            lib.ring = GlowMat("Shockwave", t.Ring, new Color(2f, 2f, 2f));
            lib.orb = GlowMat("Orb", t.Soft, new Color(2f, 2f, 2f));
            lib.beam = GlowMat("HolyBeam", t.Beam, new Color(1f, 1f, 1f), true, 1.5f);
            lib.smoke = GlowMat("Smoke", t.Smoke, new Color(0.06f, 0.055f, 0.05f, 0.7f), false);
            envMats["Rune"] = GlowMat("RuneCircle", t.Rune, new Color(0.55f, 0.24f, 0.07f));
            envMats["God"] = GlowMat("GodRay", t.God, new Color(0.05f, 0.055f, 0.075f), true, 2.2f);
        }

        // ------------------------------------------------------------ meshes

        static void MakePieceMeshes(KingsfallLibrary lib)
        {
            lib.pieceMeshes = new Mesh[7];
            lib.shards = new ShardSet[7];
            int[] shardCounts = { 0, 9, 12, 12, 12, 14, 15 };
            for (int ti = 1; ti <= 6; ti++)
            {
                var type = (PieceType)ti;
                var s = PieceSculpts.Get(type);
                var mesh = SurfaceNets.Build(s.Shape, s.Bounds, 0.0085f, p => new Color(0, s.Gold(p), 0, 1));
                mesh.name = type + "Mesh";
                SaveAsset(mesh, Gen + type + ".asset");
                lib.pieceMeshes[ti] = mesh;
                lib.shards[ti] = MakeShards(type, s, shardCounts[ti], 1000 + ti * 17);
                Debug.Log($"Kingsfall: {type} mesh {mesh.vertexCount} verts, {lib.shards[ti].meshes.Length} shards");
            }
            var (robe, skin) = PieceSculpts.Grandmaster();
            lib.grandmasterMesh = SurfaceNets.Build(robe.Shape, robe.Bounds, 0.011f, p => new Color(0, robe.Gold(p), 0, 1));
            lib.grandmasterMesh.name = "GrandmasterRobe";
            SaveAsset(lib.grandmasterMesh, Gen + "GrandmasterRobe.asset");
            lib.grandmasterSkinMesh = SurfaceNets.Build(skin.Shape, skin.Bounds, 0.006f, p => new Color(0, 0, 0, 1));
            lib.grandmasterSkinMesh.name = "GrandmasterSkin";
            SaveAsset(lib.grandmasterSkinMesh, Gen + "GrandmasterSkin.asset");
            Debug.Log($"Kingsfall: grandmaster meshes {lib.grandmasterMesh.vertexCount} + {lib.grandmasterSkinMesh.vertexCount} verts");
        }

        static ShardSet MakeShards(PieceType type, Sculpt s, int count, int seed)
        {
            var rng = new System.Random(seed);
            var sites = new List<Vector3>();
            int guard = 0;
            while (sites.Count < count && guard++ < 20000)
            {
                var b = s.Bounds;
                var p = new Vector3(
                    b.min.x + (float)rng.NextDouble() * b.size.x,
                    b.min.y + (float)Math.Pow(rng.NextDouble(), 0.85) * b.size.y,
                    b.min.z + (float)rng.NextDouble() * b.size.z);
                if (s.Shape(p) < -0.015f) sites.Add(p);
            }
            var set = new ShardSet { meshes = new Mesh[sites.Count], offsets = new Vector3[sites.Count] };
            string path = Gen + type + "Shards.asset";
            if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
            var keep = new List<Mesh>();
            var offsets = new List<Vector3>();
            for (int k = 0; k < sites.Count; k++)
            {
                int kk = k;
                Func<Vector3, float> cell = p =>
                {
                    float d = float.MinValue;
                    Vector3 sk = sites[kk];
                    for (int j = 0; j < sites.Count; j++)
                    {
                        if (j == kk) continue;
                        Vector3 sj = sites[j];
                        Vector3 n = (sj - sk).normalized;
                        float plane = Vector3.Dot(p - (sk + sj) * 0.5f, n);
                        if (plane > d) d = plane;
                    }
                    return d;
                };
                Func<Vector3, float> f = p => Mathf.Max(s.Shape(p), cell(p) + 0.004f);
                var mesh = SurfaceNets.Build(f, s.Bounds, 0.016f, p =>
                {
                    float shape = s.Shape(p), c = cell(p) + 0.004f;
                    float interior = c > shape ? 1f : 0f;
                    return new Color(interior, interior > 0.5f ? 0f : s.Gold(p), 0, 1);
                }, 2);
                if (mesh.vertexCount < 24) { UnityEngine.Object.DestroyImmediate(mesh); continue; }
                // recentre on the fragment's centroid so physics pivots look right
                var verts = mesh.vertices;
                Vector3 c0 = Vector3.zero;
                foreach (var v in verts) c0 += v;
                c0 /= verts.Length;
                for (int i = 0; i < verts.Length; i++) verts[i] -= c0;
                mesh.vertices = verts;
                mesh.RecalculateBounds();
                mesh.name = $"{type}Shard{k}";
                keep.Add(mesh);
                offsets.Add(c0);
            }
            for (int i = 0; i < keep.Count; i++)
            {
                if (i == 0) AssetDatabase.CreateAsset(keep[i], path);
                else AssetDatabase.AddObjectToAsset(keep[i], path);
            }
            set.meshes = keep.ToArray();
            set.offsets = offsets.ToArray();
            return set;
        }

        static readonly Dictionary<string, Mesh> envMeshes = new Dictionary<string, Mesh>();

        static void MakeUtilityMeshes(KingsfallLibrary lib)
        {
            lib.markerDisc = SaveMesh(Quad(1f), "MarkerQuad");
            lib.ringMesh = SaveMesh(Quad(2f), "RingQuad");
            lib.sphere = SaveMesh(UvSphere(20, 14), "GlowSphere");

            // board tile
            envMeshes["Tile"] = SaveMesh(SurfaceNets.Build(p => Sdf.Box(p, new Vector3(0, -0.06f, 0), new Vector3(0.4925f, 0.06f, 0.4925f), 0.03f),
                new Bounds(new Vector3(0, -0.06f, 0), new Vector3(1f, 0.13f, 1f)), 0.02f), "Tile");

            // plinth + frame
            Func<Vector3, float> boardBase = p =>
            {
                float plinth = Sdf.Box(p, new Vector3(0, (FloorY - 0.04f) * 0.5f, 0), new Vector3(4.95f, (-0.04f - FloorY) * 0.5f, 4.95f), 0.06f);
                // stepped skirt at the foot
                plinth = Mathf.Min(plinth, Sdf.Box(p, new Vector3(0, FloorY + 0.09f, 0), new Vector3(5.12f, 0.09f, 5.12f), 0.04f));
                float frame = Sdf.Box(p, new Vector3(0, -0.03f, 0), new Vector3(4.62f, 0.07f, 4.62f), 0.04f);
                frame = Sdf.Sub(frame, Sdf.Box(p, new Vector3(0, 0f, 0), new Vector3(4.0f, 0.3f, 4.0f), 0.0f), 0.01f);
                // carved panels on the plinth sides
                float panels = float.MaxValue;
                for (int i = -2; i <= 2; i++)
                {
                    panels = Mathf.Min(panels, Sdf.Box(new Vector3(Mathf.Abs(p.x), p.y, p.z), new Vector3(4.97f, -0.48f, i * 1.9f), new Vector3(0.05f, 0.24f, 0.75f), 0.03f));
                    panels = Mathf.Min(panels, Sdf.Box(new Vector3(p.x, p.y, Mathf.Abs(p.z)), new Vector3(i * 1.9f, -0.48f, 4.97f), new Vector3(0.75f, 0.24f, 0.05f), 0.03f));
                }
                plinth = Sdf.Sub(plinth, panels, 0.015f);
                return Mathf.Min(plinth, frame);
            };
            envMeshes["Base"] = SaveMesh(SurfaceNets.Build(boardBase, new Bounds(new Vector3(0, (FloorY + 0.05f) * 0.5f, 0), new Vector3(10.3f, -FloorY + 0.1f, 10.3f)), 0.035f), "BoardBase");

            // fluted pillar
            var pillarLathe = new Lathe(new[]
            {
                new Vector2(1.05f, 0f), new Vector2(1.05f, 0.35f), new Vector2(0.95f, 0.42f), new Vector2(0.9f, 0.52f), new Vector2(0.78f, 0.6f),
                new Vector2(0.7f, 0.72f), new Vector2(0.62f, 0.85f), new Vector2(0.6f, 1.0f), new Vector2(0.56f, 6f), new Vector2(0.52f, 11f),
                new Vector2(0.6f, 11.2f), new Vector2(0.76f, 11.5f), new Vector2(0.92f, 11.75f), new Vector2(1.05f, 11.95f), new Vector2(1.05f, 12.25f),
                new Vector2(0.92f, 12.35f), new Vector2(0.92f, 15f),
            }, 4);
            envMeshes["Pillar"] = SaveMesh(SurfaceNets.Build(p =>
            {
                float d = pillarLathe.Eval(p);
                if (p.y > 1.05f && p.y < 10.95f)
                {
                    float ang = Mathf.Atan2(p.z, p.x);
                    float fl = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ang * 20f), 3f);
                    float fade = Mathf.Clamp01((p.y - 1.05f) / 0.2f) * Mathf.Clamp01((10.95f - p.y) / 0.2f);
                    d += fl * 0.045f * fade;
                }
                return d;
            }, new Bounds(new Vector3(0, 7.5f, 0), new Vector3(2.15f, 15f, 2.15f)), 0.045f), "Pillar");

            // brazier
            var brazierLathe = new Lathe(new[]
            {
                new Vector2(0.42f, 0f), new Vector2(0.42f, 0.07f), new Vector2(0.32f, 0.12f), new Vector2(0.16f, 0.2f), new Vector2(0.11f, 0.35f),
                new Vector2(0.095f, 0.9f), new Vector2(0.13f, 1.0f), new Vector2(0.17f, 1.06f), new Vector2(0.13f, 1.12f), new Vector2(0.22f, 1.2f),
                new Vector2(0.44f, 1.32f), new Vector2(0.57f, 1.45f), new Vector2(0.6f, 1.55f), new Vector2(0.5f, 1.57f),
            }, 6);
            envMeshes["Brazier"] = SaveMesh(SurfaceNets.Build(brazierLathe.Eval, new Bounds(new Vector3(0, 0.8f, 0), new Vector3(1.3f, 1.65f, 1.3f)), 0.015f,
                p =>
                {
                    float r = new Vector2(p.x, p.z).magnitude;
                    bool coals = p.y > 1.535f && r < 0.52f;
                    bool rim = !coals && p.y > 1.4f && r > 0.5f;
                    bool collar = p.y > 0.98f && p.y < 1.14f;
                    return new Color(coals ? 1 : 0, rim || collar ? 1 : 0, 0, 1);
                }), "Brazier");

            envMeshes["GodCone"] = SaveMesh(Cone(1f, 0.15f, 1f, 40), "GodCone");
        }

        static Mesh SaveMesh(Mesh m, string name)
        {
            m.name = name;
            SaveAsset(m, Gen + name + ".asset");
            return m;
        }

        static Mesh Quad(float size)
        {
            float h = size * 0.5f;
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        static Mesh UvSphere(int seg, int rings)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var tri = new List<int>();
            for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= seg; s++)
            {
                float th = r / (float)rings * Mathf.PI, ph = s / (float)seg * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                v.Add(p * 0.5f); n.Add(p); uv.Add(new Vector2(0.5f, 0.5f)); c.Add(Color.white);
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < seg; s++)
            {
                int a = r * (seg + 1) + s, b = a + seg + 1;
                tri.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Open cone along +Z from radius r0 at z=0 to r1 at z=len; UV.v runs along the length.</summary>
        static Mesh Cone(float r1, float r0, float len, int seg)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var tri = new List<int>();
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v.Add(dir * r0); v.Add(dir * r1 + Vector3.forward * len);
                n.Add(dir); n.Add(dir);
                uv.Add(new Vector2(s / (float)seg, 0)); uv.Add(new Vector2(s / (float)seg, 1));
                c.Add(Color.white); c.Add(Color.white);
            }
            for (int s = 0; s < seg; s++)
            {
                int i = s * 2;
                tri.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 });
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------ render pipeline

        static UniversalRenderPipelineAsset MakePipeline()
        {
            string rendererPath = Root + "Settings/KingsfallRenderer.asset";
            string urpPath = Root + "Settings/KingsfallURP.asset";
            if (AssetDatabase.LoadMainAssetAtPath(urpPath)) AssetDatabase.DeleteAsset(urpPath);
            if (AssetDatabase.LoadMainAssetAtPath(rendererPath)) AssetDatabase.DeleteAsset(rendererPath);

            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, rendererPath);
            renderer.renderingMode = RenderingMode.Forward;

            var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = "SSAO";
            AssetDatabase.AddObjectToAsset(ssao, renderer);
            var sso = new SerializedObject(ssao);
            SetProp(sso, "m_Settings.Intensity", 2.2f);
            SetProp(sso, "m_Settings.Radius", 0.28f);
            SetProp(sso, "m_Settings.DirectLightingStrength", 0.35f);
            sso.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out string _, out long localId);
            var rso = new SerializedObject(renderer);
            var features = rso.FindProperty("m_RendererFeatures");
            var map = rso.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ssao;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);

            var urp = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(urp, urpPath);
            urp.renderScale = 1f;
            urp.msaaSampleCount = 4;
            urp.supportsHDR = true;
            urp.supportsCameraDepthTexture = true;
            urp.shadowDistance = 42f;
            urp.shadowCascadeCount = 2;
            urp.mainLightShadowmapResolution = 4096;
            urp.maxAdditionalLightsCount = 8;
            var so = new SerializedObject(urp);
            SetProp(so, "m_AdditionalLightShadowsSupported", true);
            SetProp(so, "m_SoftShadowsSupported", true);
            SetPropInt(so, "m_SoftShadowQuality", 3);
            SetPropInt(so, "m_AdditionalLightsShadowmapResolution", 4096);
            SetPropInt(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetPropInt(so, "m_ColorGradingMode", 1);
            SetPropInt(so, "m_HDRColorBufferPrecision", 1);
            SetProp(so, "m_ShadowDepthBias", 0.6f);
            SetProp(so, "m_ShadowNormalBias", 0.5f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);
            QualitySettings.vSyncCount = 1;
            QualitySettings.antiAliasing = 4;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            AssetDatabase.SaveAssets();
            return urp;
        }

        static void SetProp(SerializedObject so, string name, float v) { var p = so.FindProperty(name); if (p != null) p.floatValue = v; else Debug.LogWarning("Missing prop " + name); }
        static void SetProp(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; else Debug.LogWarning("Missing prop " + name); }
        static void SetPropInt(SerializedObject so, string name, int v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning("Missing prop " + name); return; }
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueFlag = v; else p.intValue = v;
        }

        static VolumeProfile MakeVolumeProfile()
        {
            string path = Root + "Settings/KingsfallGrade.asset";
            if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tm = profile.Add<Tonemapping>(); tm.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>();
            bloom.threshold.Override(0.95f); bloom.intensity.Override(0.85f); bloom.scatter.Override(0.72f);
            bloom.highQualityFiltering.Override(true); bloom.tint.Override(new Color(1f, 0.92f, 0.85f));
            var vig = profile.Add<Vignette>(); vig.intensity.Override(0.34f); vig.smoothness.Override(0.45f); vig.color.Override(new Color(0.02f, 0.01f, 0.01f));
            var ca = profile.Add<ColorAdjustments>(); ca.postExposure.Override(0.35f); ca.contrast.Override(14f); ca.saturation.Override(4f);
            var split = profile.Add<SplitToning>(); split.shadows.Override(new Color(0.34f, 0.42f, 0.58f)); split.highlights.Override(new Color(0.78f, 0.62f, 0.44f)); split.balance.Override(-15f);
            var dof = profile.Add<DepthOfField>(); dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focusDistance.Override(8f); dof.aperture.Override(5.6f); dof.focalLength.Override(50f); dof.active = false;
            var chroma = profile.Add<ChromaticAberration>(); chroma.intensity.Override(0.06f);
            var lens = profile.Add<LensDistortion>(); lens.intensity.Override(0f);
            var grain = profile.Add<FilmGrain>(); grain.type.Override(FilmGrainLookup.Thin1); grain.intensity.Override(0.12f);
            var wb = profile.Add<WhiteBalance>(); wb.temperature.Override(4f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ------------------------------------------------------------ scene

        static GameObject Obj(string name, Mesh mesh, Material mat, Vector3 pos, Transform parent = null, bool shadows = true)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.isStatic = true;
            return go;
        }

        static GameObject GoldStrip(string name, Vector3 centre, Vector3 size, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = envMats["Gold"];
            go.isStatic = true;
            return go;
        }

        static void BuildScene(KingsfallLibrary lib, Tex tex, VolumeProfile profile)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.17f, 0.24f);
            RenderSettings.ambientEquatorColor = new Color(0.09f, 0.07f, 0.06f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.025f, 0.02f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.042f;
            RenderSettings.fogColor = new Color(0.018f, 0.018f, 0.026f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 1f;

            // --- camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.012f, 0.012f, 0.018f);
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 120f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            camData.renderShadows = true;
            var rig = camGo.AddComponent<CameraRig>();
            rig.Cam = cam;
            camGo.transform.position = new Vector3(0, 9, -7);
            camGo.transform.LookAt(Vector3.zero);

            // --- lights
            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.5f, 0.6f, 0.95f);
            moon.intensity = 0.32f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.8f;
            moon.transform.rotation = Quaternion.Euler(38f, 145f, 0f);

            var key = new GameObject("Key Light").AddComponent<Light>();
            key.type = LightType.Spot;
            key.color = new Color(1f, 0.88f, 0.74f);
            key.intensity = 260f;
            key.range = 32f;
            key.spotAngle = 52f;
            key.innerSpotAngle = 30f;
            key.shadows = LightShadows.Soft;
            key.shadowResolution = LightShadowResolution.High;
            key.shadowBias = 0.02f;
            key.transform.position = new Vector3(2.0f, 13.5f, -4.5f);
            key.transform.LookAt(new Vector3(0, 0, 0.3f));
            var keyData = key.gameObject.AddComponent<UniversalAdditionalLightData>();

            var rim = new GameObject("Rim Light").AddComponent<Light>();
            rim.type = LightType.Spot;
            rim.color = new Color(0.45f, 0.55f, 1f);
            rim.intensity = 120f;
            rim.range = 30f;
            rim.spotAngle = 62f;
            rim.shadows = LightShadows.None;
            rim.transform.position = new Vector3(-3f, 9f, 10f);
            rim.transform.LookAt(new Vector3(0, 0.4f, 0));

            // --- environment root
            var env = new GameObject("Throne Hall").transform;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(env, false);
            floor.transform.position = new Vector3(0, FloorY, 0);
            floor.transform.localScale = new Vector3(9, 1, 9);
            floor.GetComponent<MeshRenderer>().sharedMaterial = envMats["Floor"];
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
            var floorCol = floor.AddComponent<BoxCollider>();
            floorCol.center = new Vector3(0, -0.5f, 0); floorCol.size = new Vector3(10, 1, 10);
            floor.isStatic = true;

            var rune = Obj("Rune Circle", lib.ringMesh, envMats["Rune"], new Vector3(0, FloorY + 0.012f, 0), env, false);
            rune.transform.localScale = new Vector3(10.5f, 1, 10.5f);
            rune.isStatic = false;

            var boardRoot = new GameObject("Board").transform;
            boardRoot.SetParent(env, false);
            var baseGo = Obj("Board Base", envMeshes["Base"], envMats["Frame"], Vector3.zero, boardRoot);
            var baseCol = baseGo.AddComponent<BoxCollider>();
            baseCol.center = new Vector3(0, (FloorY - 0.02f) * 0.5f, 0);
            baseCol.size = new Vector3(9.9f, -FloorY - 0.02f, 9.9f);
            // tile tops are at y = 0: a thin collider so fragments rest on the squares
            var topCol = baseGo.AddComponent<BoxCollider>();
            topCol.center = new Vector3(0, -0.05f, 0);
            topCol.size = new Vector3(9.2f, 0.1f, 9.2f);

            for (int s = 0; s < 64; s++)
            {
                int f = s & 7, r = s >> 3;
                bool light = ((f + r) & 1) == 1;
                Obj("Tile " + Board.SquareName(s), envMeshes["Tile"], envMats[light ? "TileLight" : "TileDark"], new Vector3(f - 3.5f, 0, r - 3.5f), boardRoot);
            }
            GoldStrip("Gold Bed", new Vector3(0, -0.06f, 0), new Vector3(8f, 0.02f, 8f), boardRoot);
            float[] trims = { 4.05f, 4.56f };
            foreach (float tr in trims)
            {
                float y = tr < 4.3f ? 0.012f : 0.03f;
                float w = tr < 4.3f ? 0.05f : 0.035f;
                GoldStrip("Trim N", new Vector3(0, y, tr), new Vector3(tr * 2f + w, 0.03f, w), boardRoot);
                GoldStrip("Trim S", new Vector3(0, y, -tr), new Vector3(tr * 2f + w, 0.03f, w), boardRoot);
                GoldStrip("Trim E", new Vector3(tr, y, 0), new Vector3(w, 0.03f, tr * 2f + w), boardRoot);
                GoldStrip("Trim W", new Vector3(-tr, y, 0), new Vector3(w, 0.03f, tr * 2f + w), boardRoot);
            }
            // coordinates
            for (int i = 0; i < 8; i++)
            {
                Label(((char)('a' + i)).ToString(), new Vector3(i - 3.5f, 0.045f, -4.31f), 0f, boardRoot, lib.headFont);
                Label(((char)('a' + i)).ToString(), new Vector3(i - 3.5f, 0.045f, 4.31f), 180f, boardRoot, lib.headFont);
                Label(((char)('1' + i)).ToString(), new Vector3(-4.31f, 0.045f, i - 3.5f), 0f, boardRoot, lib.headFont);
                Label(((char)('1' + i)).ToString(), new Vector3(4.31f, 0.045f, i - 3.5f), 180f, boardRoot, lib.headFont);
            }

            // pillars
            for (int i = 0; i < 10; i++)
            {
                float a = (i + 0.5f) / 10f * Mathf.PI * 2f;
                var p = Obj("Pillar", envMeshes["Pillar"], envMats["Pillar"], new Vector3(Mathf.Cos(a) * 13f, FloorY, Mathf.Sin(a) * 13f), env);
                var cc = p.AddComponent<CapsuleCollider>(); cc.radius = 0.9f; cc.height = 15f; cc.center = new Vector3(0, 7.5f, 0);
            }

            // braziers with fire lights
            var fires = new List<Transform>();
            var fireLights = new List<Light>();
            Vector3[] spots = { new Vector3(6.9f, 0, 6.9f), new Vector3(-6.9f, 0, 6.9f), new Vector3(6.9f, 0, -6.9f), new Vector3(-6.9f, 0, -6.9f) };
            foreach (var sp in spots)
            {
                var b = Obj("Brazier", envMeshes["Brazier"], envMats["Bronze"], new Vector3(sp.x, FloorY, sp.z), env);
                var cc = b.AddComponent<CapsuleCollider>(); cc.radius = 0.55f; cc.height = 1.6f; cc.center = new Vector3(0, 0.8f, 0);
                var anchor = new GameObject("Fire").transform;
                anchor.SetParent(b.transform, false);
                anchor.localPosition = new Vector3(0, 1.58f, 0);
                fires.Add(anchor);
                var l = new GameObject("Fire Light").AddComponent<Light>();
                l.transform.SetParent(b.transform, false);
                l.transform.localPosition = new Vector3(0, 2.3f, 0);
                l.type = LightType.Point;
                l.color = new Color(1f, 0.52f, 0.2f);
                l.intensity = 24f;
                l.range = 15f;
                // only the two far braziers cast shadows so every shadow map fits the atlas
                l.shadows = sp.z > 0 ? LightShadows.Soft : LightShadows.None;
                l.shadowResolution = LightShadowResolution.Medium;
                l.shadowBias = 0.05f;
                fireLights.Add(l);
            }

            // god ray cone along the key light
            var god = Obj("God Ray", envMeshes["GodCone"], envMats["God"], key.transform.position, env, false);
            god.transform.rotation = key.transform.rotation;
            god.transform.localScale = new Vector3(5.2f, 5.2f, 14.5f);
            god.isStatic = false;

            // reflection probe
            var probeGo = new GameObject("Reflection Probe");
            probeGo.transform.position = new Vector3(0, 1.0f, 0);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size = new Vector3(34, 16, 34);
            probe.resolution = 256;
            probe.hdr = true;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = new Color(0.01f, 0.01f, 0.015f);
            probe.intensity = 1f;
            probe.nearClipPlane = 0.1f;
            probe.farClipPlane = 60f;

            // volume
            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            // game root
            var game = new GameObject("Kingsfall");
            game.AddComponent<SoundBank>();
            var post = game.AddComponent<PostFx>(); post.Volume = vol;
            game.AddComponent<Fx>();
            game.AddComponent<BoardView>();
            game.AddComponent<Choreographer>();
            game.AddComponent<GameUI>();
            game.AddComponent<Speech>();
            game.AddComponent<PieceVoices>();
            game.AddComponent<Grandmaster>();
            var amb = game.AddComponent<Ambience>();
            amb.Fires = fires.ToArray();
            amb.FireLights = fireLights.ToArray();
            amb.Rune = rune.transform;
            amb.Probe = probe;
            game.AddComponent<GameController>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void Label(string text, Vector3 pos, float yaw, Transform parent, Font font)
        {
            var go = new GameObject("Coord " + text);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = font;
            tm.fontSize = 96;
            tm.characterSize = 0.028f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.95f, 0.72f, 0.38f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
