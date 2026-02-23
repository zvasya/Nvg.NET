using System;

namespace SilkyNvg.Text
{
    public struct TextRow
    {

        /// <summary>
        /// Input text from where row starts.
        /// </summary>
        public ReadOnlyMemory<Char> Start { get; internal set; }

        /// <summary>
        /// The input text where the row ends (one past the last character).
        /// </summary>
        public ReadOnlyMemory<Char> End { get; internal set; }

        /// <summary>
        /// Beginning, and rest of, the next row.
        /// </summary>
        public ReadOnlyMemory<Char> Next { get; internal set; }

        /// <summary>
        /// Logical width of the row.
        /// </summary>
        public float Width { get; internal set; }

        /// <summary>
        /// Actual least X-bound of the row. Logical with and bounds can differ because of kerning and some parts over extending.
        /// </summary>
        public float MinX { get; internal set; }

        /// <summary>
        /// Actual largest X-bound of the row. Logical with and bounds can differ because of kerning and some parts over extending.
        /// </summary>
        public float MaxX { get; internal set; }

    }
}
