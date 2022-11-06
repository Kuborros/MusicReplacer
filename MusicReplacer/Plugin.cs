using BepInEx;
using System.IO;
using System.Text;
using System;
using UnityEngine;
using HarmonyLib;
using System.Collections.Generic;

namespace MusicReplacer
{
    [BepInPlugin("com.kuborro.plugins.fp2.musicreplacer", "MusicReplacerMod", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static string AudioPath = Path.Combine(Path.GetFullPath("."), "mod_overrides\\MusicReplacements");
        public static Dictionary<string,string> AudioTracks = new();
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
            string[] files = Directory.GetFiles(AudioPath);
            foreach (string file in files)
            {
                Logger.LogDebug("File located: " + file);
                AudioTracks.Add(Path.GetFileNameWithoutExtension(file).ToLower(), file);
                Logger.LogInfo("Added replacement track: " + Path.GetFileNameWithoutExtension(file).ToLower());
            }

        }


        private void Awake()
        {
            Directory.CreateDirectory(AudioPath);
            DirectoryScan(AudioPath);

            var harmony = new Harmony("com.kuborro.plugins.fp2.musicreplacer");
            harmony.PatchAll(typeof(Patch));
        }
    }

    class Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(FPAudio), nameof(FPAudio.PlayMusic), new Type[] { typeof(AudioClip), typeof(float) })]
        static void Prefix(ref AudioClip bgmMusic)
        {
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
                        int i = 1;
                    }
                    selectedClip.name = bgmMusic.name;
                    bgmMusic = selectedClip;
                }

            }
        }
    }
}
