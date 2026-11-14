using System.Collections.Generic;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// One conversation: who is speaking and what they say, line by line. The circle is mute, so a
    /// conversation is one speaker talking; each line is one page of the dialogue box.
    ///
    /// Create one with Assets > Create > Project Circle > Dialogue and type the lines in the Inspector.
    /// </summary>
    [CreateAssetMenu(fileName = "Dialogue", menuName = "Project Circle/Dialogue")]
    public sealed class Dialogue : ScriptableObject
    {
        [Tooltip("Name shown above the text.")]
        [SerializeField] private string speaker = "Old Triangle";

        [Tooltip("Each entry is one page. Keep pages to two short lines or so.")]
        [SerializeField, TextArea(2, 4)] private string[] lines = { "..." };

        public string Speaker => speaker;
        public IReadOnlyList<string> Lines => lines;
    }
}
