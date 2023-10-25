using BepInEx;
using System.IO;
using System.Text;
using System;
using UnityEngine;
using HarmonyLib;
using System.Collections.Generic;
using Object = UnityEngine.Object;
using System.Reflection.Emit;
using BepInEx.Logging;
using BepInEx.Configuration;

namespace MusicReplacer
{
    [BepInPlugin("com.kuborro.plugins.fp2.musicreplacer", "MusicReplacerMod", "1.2.0")]
    [BepInProcess("FP2.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public static string AudioPath = Path.Combine(Paths.ExecutablePath, "mod_overrides\\MusicReplacements");
        public static string SFXPath = Path.Combine(Paths.ExecutablePath, "mod_overrides\\SFXReplacements");
        public static Dictionary<string, AuxTrack> AudioTracks = new();
        public static Dictionary<string, AudioClip> SFXTracks = new();
        public static AudioClip LastTrack;
        public static ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource("Music Replacer");


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
                    uri.Append(string.Format("%{0:X2}", (int)v));
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
            return extension switch
            {
                ".wav" => AudioType.WAV,
                ".ogg" => AudioType.OGGVORBIS,
                ".s3m" => AudioType.S3M,
                ".mod" => AudioType.MOD,
                ".it" => AudioType.IT,
                ".xm" => AudioType.XM,
                ".aiff" or ".aif" => AudioType.AIFF,
                ".mp3" => AudioType.MPEG,
                _ => AudioType.UNKNOWN,
            };
        }

        void DirectoryScan(string path)
        {
            string[] files = Directory.GetFiles(path);
            foreach (string file in files)
            {
                Logger.LogDebug("File located: " + file);
                string trackName = Path.GetFileNameWithoutExtension(file).ToLower();

                ConfigEntry<float> confLoopStart = Config.Bind(trackName,"Loop start",0f);
                ConfigEntry<float> confLoopEnd = Config.Bind(trackName, "Loop End", 0f);

                AuxTrack auxTrack = new AuxTrack
                {
                    Name = trackName,
                    FilePath = file,
                    LoopStart = confLoopStart.Value,
                    LoopEnd = confLoopEnd.Value,
                    Type = GetAudioType(Path.GetExtension(file))
                };

                AudioTracks.Add(auxTrack.Name, auxTrack);
                logger.LogInfo("Added replacement track: " + auxTrack.Name);
                if (auxTrack.Type == AudioType.MPEG)
                {
                    logger.LogWarning("Warning! MP3 file support in this version of Unity is very limited, your audio might not work!");
                }
            }
        }

        void SFXScan(string path)
        {
            string[] files = Directory.GetFiles(path);
            foreach (string file in files)
            {
                logger.LogDebug("SFX File located: " + file);
                string ext = Path.GetExtension(file);
                if (File.Exists(file) && (GetAudioType(ext) == AudioType.OGGVORBIS || GetAudioType(ext) == AudioType.WAV))
                {
                    using (WWW audioLoader = new(FilePathToFileUrl(file)))
                    {
                        AudioClip SFXClip = audioLoader.GetAudioClip(false, false, GetAudioType(ext));
                        while (!(SFXClip.loadState == AudioDataLoadState.Loaded))
                        {
                            System.Threading.Thread.Sleep(1);
                        }
                        SFXClip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        SFXTracks.Add(Path.GetFileNameWithoutExtension(file).ToLower(), SFXClip);
                        logger.LogInfo("Added replacement SFX: " + Path.GetFileNameWithoutExtension(file).ToLower());
                    }
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
            harmony.PatchAll(typeof(PatchCutscenePlayer));
        }
    }

    class PatchMusicPlayer
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(FPAudio), nameof(FPAudio.PlayMusic), new Type[] { typeof(AudioClip), typeof(float) })]
        static void PlayMusicPrefix(ref AudioClip bgmMusic, out AuxTrack __state)
        {
            __state = null;
            if (Plugin.LastTrack != null)
            {
                Object.Destroy(Plugin.LastTrack);
                Plugin.LastTrack = null;
            }

            if (bgmMusic == null) return;
            if (Plugin.AudioTracks.ContainsKey(bgmMusic.name.ToLower()))
            {
                AuxTrack track = Plugin.AudioTracks[bgmMusic.name.ToLower()];
                string ext = Path.GetExtension(track.FilePath);
                bool stream = true;
                if (ext == "")
                {
                    ext = ".wav";
                    track.FilePath += ext;
                }
                if (ext is ".mod" or ".s3m" or ".it" or ".xm")
                {
                    stream = false;
                }
                if (File.Exists(track.FilePath) && track.Type != AudioType.UNKNOWN)
                {
                    using (WWW audioLoader = new(Plugin.FilePathToFileUrl(track.FilePath)))
                    {
                        while (!audioLoader.isDone)
                        {
                            System.Threading.Thread.Sleep(1);
                        }
                        AudioClip selectedClip = audioLoader.GetAudioClip(false, stream, Plugin.GetAudioType(ext));
                        while (!(selectedClip.loadState == AudioDataLoadState.Loaded))
                        {
                            System.Threading.Thread.Sleep(1);
                        }
                        selectedClip.name = (bgmMusic.name + "_modded").ToLower();
                        __state = track;
                        bgmMusic = selectedClip;
                        Plugin.LastTrack = selectedClip;
                    }
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FPAudio), nameof(FPAudio.PlayMusic), new Type[] { typeof(AudioClip), typeof(float) })]
        static void PlayMusicPostfix(AuxTrack __state, ref float ___loopStart, ref float ___loopEnd)
        {
            if (__state == null) return;
            Plugin.logger.LogDebug("Loaded track loop data for:" + __state.Name + "\n Loop start:" + __state.LoopStart + "\n Loop End:" + __state.LoopEnd);
            ___loopEnd = __state.LoopEnd; 
            ___loopStart = __state.LoopStart;
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

    class PatchCutscenePlayer
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.clip), MethodType.Setter)]
        static void Prefix(ref AudioClip value)
        {
            if (value == null) return;
            if (Plugin.SFXTracks.ContainsKey(value.name.ToLower()))
            {
                Plugin.SFXTracks[value.name.ToLower()].name = value.name;
                value = Plugin.SFXTracks[value.name.ToLower()];
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
        static IEnumerable<CodeInstruction> MergaPhaseTranspiler(IEnumerable<CodeInstruction> instructions)
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
