using BepInEx;
using System.IO;
using System.Text;
using System;
using UnityEngine;
using HarmonyLib;
using System.Collections.Generic;
using Object = UnityEngine.Object;
using System.Reflection.Emit;
using System.Linq;

namespace MusicReplacer
{
    [BepInPlugin("com.kuborro.plugins.fp2.musicreplacer", "MusicReplacerMod", "1.1.1")]
    [BepInProcess("FP2.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public static string AudioPath = Path.Combine(Path.GetFullPath("."), "mod_overrides\\MusicReplacements");
        public static string SFXPath = Path.Combine(Path.GetFullPath("."), "mod_overrides\\SFXReplacements");
        public static Dictionary<string, string> AudioTracks = new();
        public static Dictionary<string, AudioClip> SFXTracks = new();
        public static AudioClip LastTrack;
        public static string FilePathToFileUrl(string filePath)
        {
            StringBuilder uri = new();
            foreach (char v in filePath)
            {
                if ((v >= 'a' && v <= 'z') || (v >= 'A' && v <= 'Z') || (v >= '0' && v <= '9') ||
                  v == '+' || v == '/' || v == ':' || v == '.' || v == '-' || v == '_' || v == '~' ||
                  v > '\xFF')
                {
                    uri.Append(v);
                }
                else if (v == Path.DirectorySeparatorChar || v == Path.AltDirectorySeparatorChar)
                {
                    uri.Append('/');
                }
                else
                {
                    uri.Append(String.Format("%{0:X2}", (int)v));
                }
            }
            if (uri.Length >= 2 && uri[0] == '/' && uri[1] == '/') // UNC path
                uri.Insert(0, "file:");
            else
                uri.Insert(0, "file:///");
            return uri.ToString();
        }
        public static AudioType GetAudioType(string extension)
        {
            if (extension == ".wav") return AudioType.WAV;
            if (extension == ".ogg") return AudioType.OGGVORBIS;
            if (extension == ".s3m") return AudioType.S3M;
            if (extension == ".mod") return AudioType.MOD;
            if (extension == ".it") return AudioType.IT;
            if (extension == ".xm") return AudioType.XM;
            if (extension == ".aiff" || extension == ".aif") return AudioType.AIFF;
            return AudioType.UNKNOWN;
        }

        void DirectoryScan(string path)
        {
            string[] files = Directory.GetFiles(path);
            foreach (string file in files)
            {
                Logger.LogDebug("File located: " + file);
                AudioTracks.Add(Path.GetFileNameWithoutExtension(file).ToLower(), file);
                Logger.LogInfo("Added replacement track: " + Path.GetFileNameWithoutExtension(file).ToLower());
            }

        }

        void SFXScan(string path)
        {
            string[] files = Directory.GetFiles(path);
            foreach (string file in files)
            {
                Logger.LogDebug("SFX File located: " + file);
                string ext = Path.GetExtension(file);
                if (File.Exists(file) && (GetAudioType(ext) == AudioType.OGGVORBIS || GetAudioType(ext) == AudioType.WAV))
                {
                    WWW audioLoader = new(FilePathToFileUrl(file));
                    AudioClip SFXClip = audioLoader.GetAudioClip(false, false, GetAudioType(ext));
                    while (!(SFXClip.loadState == AudioDataLoadState.Loaded))
                    {
                        System.Threading.Thread.Sleep(1);
                    }
                    SFXClip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    SFXTracks.Add(Path.GetFileNameWithoutExtension(file).ToLower(), SFXClip);
                    Logger.LogInfo("Added replacement SFX: " + Path.GetFileNameWithoutExtension(file).ToLower());
                }
            }
        }

        private void Awake()
        {
            Directory.CreateDirectory(AudioPath);
            DirectoryScan(AudioPath);
            Directory.CreateDirectory(SFXPath);
            SFXScan(SFXPath);

            var harmony = new Harmony("com.kuborro.plugins.fp2.musicreplacer");
            harmony.PatchAll(typeof(PatchMusicPlayer));
            harmony.PatchAll(typeof(PatchSFXPlayer));
            harmony.PatchAll(typeof(PatchMergaFight));
        }
    }

    class PatchMusicPlayer
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(FPAudio), nameof(FPAudio.PlayMusic), new Type[] { typeof(AudioClip), typeof(float) })]
        static void Prefix(ref AudioClip bgmMusic,ref FPAudioLoopData[] ___musicLoopPoints)
        {
            if (Plugin.LastTrack != null)
            {
                Object.Destroy(Plugin.LastTrack);
                Plugin.LastTrack = null;
            }

            if (bgmMusic == null) return;
            if (Plugin.AudioTracks.ContainsKey(bgmMusic.name.ToLower()))
            {
                string trackPath = Plugin.AudioTracks[bgmMusic.name.ToLower()];
                string ext = Path.GetExtension(trackPath);
                bool stream = true;
                if (ext == "")
                {
                    ext = ".wav";
                    trackPath += ext;
                }
                if (ext is ".mod" or ".s3m" or ".it" or ".xm")
                {
                    stream = false;
                }
                if (File.Exists(trackPath) && Plugin.GetAudioType(ext) != AudioType.UNKNOWN)
                {
                    WWW audioLoader = new(Plugin.FilePathToFileUrl(trackPath));
                    AudioClip selectedClip = audioLoader.GetAudioClip(false, stream, Plugin.GetAudioType(ext));
                    while (!(selectedClip.loadState == AudioDataLoadState.Loaded))
                    {
                        System.Threading.Thread.Sleep(1);
                    }
                    selectedClip.name = bgmMusic.name;

                    for (int i = 0; i < ___musicLoopPoints.Length; i++)
                    {
                        if (___musicLoopPoints[i].name == bgmMusic.name)
                        {
                            ___musicLoopPoints[i].loopStart = 0;
                            ___musicLoopPoints[i].loopEnd = bgmMusic.length;
                        }
                    }
                    bgmMusic = selectedClip;
                    Plugin.LastTrack = selectedClip;
                }

            }
        }
    }

    class PatchSFXPlayer
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.PlayOneShot), new Type[] { typeof(AudioClip), typeof(float) })]
        static void Prefix(ref AudioClip clip)
        {
            if (clip == null) return;
            if (Plugin.SFXTracks.ContainsKey(clip.name.ToLower()))
            {
                Plugin.SFXTracks[clip.name.ToLower()].name = clip.name;
                clip = Plugin.SFXTracks[clip.name.ToLower()];
            }

        }
    }

    class PatchMergaFight
    {
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(MergaBlueMoon), "Activate", MethodType.Normal)]
        [HarmonyPatch(typeof(MergaBloodMoon), "Activate", MethodType.Normal)]
        [HarmonyPatch(typeof(MergaSupermoon), "Activate", MethodType.Normal)]
        [HarmonyPatch(typeof(MergaLilith), "Activate", MethodType.Normal)]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);
            for (int i = 5; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Brfalse && codes[i - 1].opcode == OpCodes.Call && codes[i - 2].opcode == OpCodes.Ldfld && codes[i - 3].opcode == OpCodes.Ldarg_0 && codes[i - 4].opcode == OpCodes.Call)
                {
                    codes[i - 3].opcode = OpCodes.Nop; //Ldarg.0
                    codes[i - 2].opcode = OpCodes.Nop; //Ldfld
                    codes[i - 1].opcode = OpCodes.Nop; //Call
                    codes[i].opcode = OpCodes.Brtrue; //BrFalse
                    break;
                };
            }
            return codes;
        }
    }
}
