using UnityEngine;

namespace MusicReplacer
{
    public class AuxTrack
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public AudioClip AudioClip { get; set; }
        public bool Preloaded { get; set; }
        public float LoopStart { get; set; }
        public float LoopEnd { get; set; }
        public AudioType Type { get; set; }
    }
}
