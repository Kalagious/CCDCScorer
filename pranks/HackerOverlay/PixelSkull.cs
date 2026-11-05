using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HackerOverlay
{
    /// <summary>
    /// Builds a blocky, pixel-art skull out of Rectangle "pixels" on a Canvas —
    /// a real 8-bit skull shape, not ASCII art — so it reads clearly at any size
    /// and can be shaken/animated like a normal visual element.
    /// </summary>
    internal static class PixelSkull
    {
        // 1 = filled pixel, 0 = empty. 16 columns x 16 rows.
        private static readonly string[] Grid =
        {
            "0000111111110000",
            "0011111111111100",
            "0111111111111110",
            "0111111111111110",
            "1111111111111111",
            "1111111111111111",
            "1111001111001111",
            "1111001111001111",
            "1111001111001111",
            "1111111111111111",
            "1111111111111111",
            "0111111111111110",
            "0111111111111110",
            "0011000000001100",
            "0001100000011000",
            "0000011111100000",
        };

        public static void Build(Canvas canvas, Brush fill, double cellSize = 18, double gap = 2)
        {
            canvas.Children.Clear();
            int rows = Grid.Length;
            int cols = Grid[0].Length;

            canvas.Width = cols * cellSize;
            canvas.Height = rows * cellSize;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (Grid[r][c] != '1') continue;

                    var rect = new Rectangle
                    {
                        Width = cellSize - gap,
                        Height = cellSize - gap,
                        Fill = fill,
                    };
                    Canvas.SetLeft(rect, c * cellSize);
                    Canvas.SetTop(rect, r * cellSize);
                    canvas.Children.Add(rect);
                }
            }
        }
    }
}
