using System.Drawing;
using System.Text.RegularExpressions;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services;

/// <summary>
/// Service for generating alignment distribution charts from user sentiment data.
/// Renders 3×3 D&D alignment grids with color-coded intensity visualization.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class AlignmentChartService
{
    private readonly ILogger<AlignmentChartService> _logger;

    // Chart configuration constants
    private const int ChartWidth = 600;
    private const int ChartHeight = 600;
    private const int GridSize = 3;
    private const int CellPadding = 10;
    private const int TitleFontSize = 16;
    private const int LabelFontSize = 12;
    private const int AxisFontSize = 10;
    private const double TrueNeutralIntensity = 0.3;

    // Mapping of display labels to alignment types for consistent matching
    private readonly Dictionary<string, AlignmentType> _alignmentLabelMap = new()
    {
        { "Lawful\nGood", AlignmentType.LawfulGood },
        { "Neutral\nGood", AlignmentType.NeutralGood },
        { "Chaotic\nGood", AlignmentType.ChaoticGood },
        { "Lawful\nNeutral", AlignmentType.LawfulNeutral },
        { "True\nNeutral", AlignmentType.TrueNeutral },
        { "Chaotic\nNeutral", AlignmentType.ChaoticNeutral },
        { "Lawful\nEvil", AlignmentType.LawfulEvil },
        { "Neutral\nEvil", AlignmentType.NeutralEvil },
        { "Chaotic\nEvil", AlignmentType.ChaoticEvil },
    };

    public AlignmentChartService(ILogger<AlignmentChartService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates an alignment chart PNG as bytes from user alignment score data.
    /// Creates a 600×600 heatmap with 3×3 grid of D&D alignments.
    /// </summary>
    /// <param name="alignmentScore">User alignment data. Cannot be null.</param>
    /// <param name="cancellationToken">Cancellation token for async operation.</param>
    /// <returns>PNG image bytes (600×600px).</returns>
    /// <exception cref="ArgumentNullException">Thrown when alignmentScore is null.</exception>
    /// <remarks>Rendering may take 50-100ms. Consider caching results for frequently accessed users.</remarks>
    public async Task<byte[]> GenerateAlignmentChartAsync(
        UserAlignmentScore alignmentScore,
        CancellationToken cancellationToken = default)
    {
        if (alignmentScore == null)
            throw new ArgumentNullException(nameof(alignmentScore), "Alignment score data is required.");

        return await Task.Run(
            () => GenerateAlignmentChart(alignmentScore),
            cancellationToken
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates an alignment chart and returns it as a base64-encoded string.
    /// Useful for embedding in JSON responses or Discord embeds.
    /// </summary>
    /// <param name="alignmentScore">User alignment data. Cannot be null.</param>
    /// <param name="cancellationToken">Cancellation token for async operation.</param>
    /// <returns>Base64-encoded PNG string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when alignmentScore is null.</exception>
    public async Task<string> GenerateAlignmentChartBase64Async(
        UserAlignmentScore alignmentScore,
        CancellationToken cancellationToken = default)
    {
        var pngBytes = await GenerateAlignmentChartAsync(alignmentScore, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(pngBytes);
    }

    private byte[] GenerateAlignmentChart(UserAlignmentScore alignmentScore)
    {
        try
        {
            // Create bitmap with proper disposal
            using (var bitmap = new Bitmap(ChartWidth, ChartHeight))
            using (var g = Graphics.FromImage(bitmap))
            {
                // Set background to white
                g.Clear(Color.White);

                // Define grid positions and alignments
                var alignments = new[]
                {
                    ("Lawful\nGood", alignmentScore.LawfulGoodCount, 0, 0),
                    ("Neutral\nGood", alignmentScore.NeutralGoodCount, 1, 0),
                    ("Chaotic\nGood", alignmentScore.ChaoticGoodCount, 2, 0),
                    ("Lawful\nNeutral", alignmentScore.LawfulNeutralCount, 0, 1),
                    ("True\nNeutral", alignmentScore.TrueNeutralCount, 1, 1),
                    ("Chaotic\nNeutral", alignmentScore.ChaoticNeutralCount, 2, 1),
                    ("Lawful\nEvil", alignmentScore.LawfulEvilCount, 0, 2),
                    ("Neutral\nEvil", alignmentScore.NeutralEvilCount, 1, 2),
                    ("Chaotic\nEvil", alignmentScore.ChaoticEvilCount, 2, 2),
                };

                // Calculate total for normalization (excluding TrueNeutral)
                int totalExcludingTrueNeutral = alignments
                    .Where(a => !a.Item1.Contains("True"))
                    .Sum(a => a.Item3);

                if (totalExcludingTrueNeutral == 0)
                    totalExcludingTrueNeutral = 1;

                int cellSize = ChartWidth / GridSize;
                var font = new Font("Arial", AxisFontSize, FontStyle.Bold);
                var labelFont = new Font("Arial", LabelFontSize);
                var titleFont = new Font("Arial", TitleFontSize, FontStyle.Bold);

                try
                {
                    // Draw grid
                    for (int i = 0; i < GridSize + 1; i++)
                    {
                        g.DrawLine(Pens.Gray, i * cellSize, 0, i * cellSize, ChartHeight);
                        g.DrawLine(Pens.Gray, 0, i * cellSize, ChartWidth, i * cellSize);
                    }

                    // Draw title
                    g.DrawString("Alignment Distribution", titleFont, Brushes.Black, new PointF(10, 10));

                    // Draw axis labels
                    g.DrawString("Lawful ← → Chaotic", font, Brushes.Black, new PointF(ChartWidth / 2 - 60, ChartHeight - 25));
                    g.DrawString("Good", font, Brushes.Black, new PointF(ChartWidth - 50, 20));
                    g.DrawString("Evil", font, Brushes.Black, new PointF(ChartWidth - 50, ChartHeight - 40));

                    // Draw each quadrant
                    for (int i = 0; i < alignments.Length; i++)
                    {
                        var (label, count, col, row) = alignments[i];
                        int cellX = col * cellSize;
                        int cellY = row * cellSize;

                        // Calculate color intensity using enum mapping for consistency
                        double intensity = TrueNeutralIntensity;
                        if (_alignmentLabelMap.TryGetValue(label, out var alignmentType))
                        {
                            if (alignmentType != AlignmentType.TrueNeutral)
                            {
                                intensity = (double)count / totalExcludingTrueNeutral;
                                intensity = Math.Max(0, Math.Min(1, intensity));
                            }
                        }

                        // Get color based on alignment type and intensity
                        Color boxColor = GetAlignmentColor(alignmentType, intensity);

                        // Draw filled rectangle
                        using (var brush = new SolidBrush(boxColor))
                        {
                            g.FillRectangle(brush, cellX + CellPadding, cellY + CellPadding, 
                                cellSize - 2 * CellPadding, cellSize - 2 * CellPadding);
                        }

                        // Draw border
                        g.DrawRectangle(Pens.Black, cellX + CellPadding, cellY + CellPadding, 
                            cellSize - 2 * CellPadding, cellSize - 2 * CellPadding);

                        // Draw label text
                        var textSize = g.MeasureString(label, labelFont);
                        float textX = cellX + (cellSize - textSize.Width) / 2;
                        float textY = cellY + cellSize / 3;
                        g.DrawString(label, labelFont, Brushes.Black, textX, textY);

                        // Draw count text
                        string countText = count.ToString();
                        var countSize = g.MeasureString(countText, labelFont);
                        float countX = cellX + (cellSize - countSize.Width) / 2;
                        float countY = cellY + cellSize * 2 / 3;
                        g.DrawString(countText, labelFont, Brushes.Black, countX, countY);
                    }
                }
                finally
                {
                    titleFont.Dispose();
                    labelFont.Dispose();
                    font.Dispose();
                }

                // Save to PNG
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating alignment chart");
            throw;
        }
    }

    /// <summary>
    /// Maps alignment type to appropriate color with intensity interpolation.
    /// </summary>
    private System.Drawing.Color GetAlignmentColor(AlignmentType alignmentType, double intensity)
    {
        // Base colors for each alignment
        System.Drawing.Color baseColor = alignmentType switch
        {
            AlignmentType.LawfulGood => System.Drawing.Color.FromArgb(0, 200, 100),      // Green
            AlignmentType.NeutralGood => System.Drawing.Color.FromArgb(0, 220, 150),     // Light Green
            AlignmentType.ChaoticGood => System.Drawing.Color.FromArgb(100, 200, 50),    // Yellow-Green
            AlignmentType.LawfulNeutral => System.Drawing.Color.FromArgb(150, 150, 255), // Blue
            AlignmentType.TrueNeutral => System.Drawing.Color.FromArgb(150, 150, 150),   // Gray
            AlignmentType.ChaoticNeutral => System.Drawing.Color.FromArgb(255, 150, 150), // Light Red
            AlignmentType.LawfulEvil => System.Drawing.Color.FromArgb(255, 100, 100),    // Red
            AlignmentType.NeutralEvil => System.Drawing.Color.FromArgb(255, 50, 50),     // Dark Red
            AlignmentType.ChaoticEvil => System.Drawing.Color.FromArgb(200, 0, 0),       // Dark Red
            _ => System.Drawing.Color.FromArgb(128, 128, 128) // Default gray
        };

        // Interpolate color based on intensity with proper rounding
        int r = (int)Math.Round(baseColor.R * intensity + 255 * (1 - intensity));
        int g = (int)Math.Round(baseColor.G * intensity + 255 * (1 - intensity));
        int b = (int)Math.Round(baseColor.B * intensity + 255 * (1 - intensity));

        // Apply alpha for de-emphasis of TrueNeutral
        int alpha = alignmentType == AlignmentType.TrueNeutral ? 128 : 255;

        return System.Drawing.Color.FromArgb(alpha, r, g, b);
    }
}
