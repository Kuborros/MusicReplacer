using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MusicReplacer
{
    [BepInPlugin("com.kuborro.plugins.fp2.musicreplacer", "MusicReplacerMod", "2.0.0")]
    [BepInProcess("FP2.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public static string AudioPath = Path.Combine(Paths.GameRootPath, "mod_overrides\\MusicReplacements");
        public static string SFXPath = Path.Combine(Paths.GameRootPath, "mod_overrides\\SFXReplacements");
        internal static ConfigFile loopConfig = new ConfigFile(Path.Combine(Paths.GameRootPath, "mod_overrides\\MusicReplacements\\custom_loops.cfg"), true);
        internal static ConfigEntry<bool> preloadTracks;
        public static Dictionary<string, AuxTrack> AudioTracks = new();
        public static Dictionary<string, AudioClip> SFXTracks = new();
        internal static string currLanguage = "english";
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
            if (preloadTracks.Value) logger.LogMessage("Preloading tracks into memory! This will take a moment.");

            string[] files = Directory.GetFiles(path);
            foreach (string file in files)
            {
                string ext = Path.GetExtension(file);
                Logger.LogDebug("File located: " + file);

                if (GetAudioType(Path.GetExtension(file)) != AudioType.UNKNOWN)
                {
                    string trackName = Path.GetFileNameWithoutExtension(file).ToLower();

                    ConfigEntry<float> confLoopStart = loopConfig.Bind(trackName, "Loop start", 0f);
                    ConfigEntry<float> confLoopEnd = loopConfig.Bind(trackName, "Loop End", 0f);

                    AuxTrack auxTrack = new AuxTrack
                    {
                        Name = trackName,
                        FilePath = file,
                        LoopStart = confLoopStart.Value,
                        LoopEnd = confLoopEnd.Value,
                        Preloaded = false,
                        Type = GetAudioType(ext)
                    };

                    if (preloadTracks.Value)
                    {
                        try
                        {
                            using (WWW audioLoader = new(FilePathToFileUrl(file)))
                            {
                                while (!audioLoader.isDone)
                                {
                                    System.Threading.Thread.Sleep(1);
                                }
                                AudioClip selectedClip = audioLoader.GetAudioClip(false, false, auxTrack.Type);
                                while (!(selectedClip.loadState == AudioDataLoadState.Loaded))
                                {
                                    System.Threading.Thread.Sleep(1);
                                }
                                auxTrack.AudioClip = selectedClip;
                            }
                            auxTrack.Preloaded = true;
                        }
                        catch (Exception e)
                        {
                            logger.LogError("Issue encountered while preloading track: " + e.Message);
                        }
                    }
                    AudioTracks.Add(auxTrack.Name, auxTrack);
                    logger.LogInfo("Added replacement track: " + auxTrack.Name);
                    if (auxTrack.Type == AudioType.MPEG)
                    {
                        logger.LogWarning("Warning! MP3 file support in this version of Unity is very limited, your audio might not work!");
                    }
                }
            }
        }

        void SFXScan(string path)
        {
            string[] files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                logger.LogDebug("SFX File located: " + file);
                string subdir = file.Replace(path, "").Replace(Path.GetFileName(file), "").Replace("\\", "").Replace("/","").ToLower();
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
                        if (!subdir.IsNullOrWhiteSpace())
                        {
                            logger.LogDebug("SFX located in subdirectory: " + subdir + ", language tag applied.");
                            SFXTracks.Add(Path.GetFileNameWithoutExtension(file).ToLower() + "_" + subdir, SFXClip);
                        }
                        else SFXTracks.Add(Path.GetFileNameWithoutExtension(file).ToLower(), SFXClip);
                        logger.LogInfo("Added replacement SFX: " + Path.GetFileNameWithoutExtension(file).ToLower());
                    }
                }
            }
        }

        private void Awake()
        {
            preloadTracks = Config.Bind("Music Playback", "Preload Music", false, "Preload music tracks into memory on startup.\n" +
            "Greatly extends memory usage and load times, prevents load stutter on slower HDD and massive files.\n" +
            "When false, tracks are streamed on-demand from HDD.");

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

                if (track.Preloaded && track.AudioClip != null)
                {
                    AudioClip selectedClip = track.AudioClip;
                    selectedClip.name = (bgmMusic.name + "_modded").ToLower();
                    __state = track;
                    bgmMusic = selectedClip;
                    Plugin.LastTrack = null;
                    return;
                }

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

            //FOR TESTING ONLY, replace with fp2lib call when available
            Plugin.currLanguage = "polish";
            if (clip == null) return;

            //Language specific SFX
            if (Plugin.SFXTracks.ContainsKey(clip.name.ToLower() + "_" + Plugin.currLanguage))
            {
                Plugin.logger.LogDebug("Playing language specific sfx for lang: " + Plugin.currLanguage);
                Plugin.SFXTracks[clip.name.ToLower() + "_" + Plugin.currLanguage].name = clip.name;
                clip = Plugin.SFXTracks[clip.name.ToLower() + "_" + Plugin.currLanguage];
                return;
            }
            //Fallback to non-language ones
            else if (Plugin.SFXTracks.ContainsKey(clip.name.ToLower()))
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
            //FOR TESTING ONLY, replace with fp2lib call when available
            Plugin.currLanguage = "polish";
            if (value == null) return;
            
            //Language specific SFX
            if (Plugin.SFXTracks.ContainsKey(value.name.ToLower() + "_" + Plugin.currLanguage))
            {
                Plugin.logger.LogDebug("Playing language specific sfx for lang: " + Plugin.currLanguage);
                Plugin.SFXTracks[value.name.ToLower() + "_" + Plugin.currLanguage].name = value.name;
                value = Plugin.SFXTracks[value.name.ToLower() + "_" + Plugin.currLanguage];
                return;
            }
            //Fallback to non-language specific
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
