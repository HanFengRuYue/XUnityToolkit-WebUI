namespace XUnityToolkit_WebUI.Services;

/// <summary>
/// Generates SDF bitmaps from FreeType antialiased bitmaps using the
/// Felzenszwalb squared Euclidean distance transform, replicating Unity's FontEngine approach.
/// </summary>
public static class DistanceFieldGenerator
{
    private const float INF = 1e20f;

    /// <summary>
    /// Generate an SDF bitmap from an antialiased FreeType bitmap.
    /// Output includes padding on all sides.
    /// </summary>
    /// <param name="bitmap">Input AA bitmap (FT_RENDER_MODE_NORMAL output)</param>
    /// <param name="bmpWidth">Input bitmap width</param>
    /// <param name="bmpHeight">Input bitmap height</param>
    /// <param name="padding">Padding in target space (pixels)</param>
    /// <param name="upsampling">1 for SDFAA, 8/16/32 for upsampled modes</param>
    /// <param name="sdfWidth">Output: SDF bitmap width (includes padding)</param>
    /// <param name="sdfHeight">Output: SDF bitmap height (includes padding)</param>
    /// <returns>SDF bitmap bytes (Alpha8, 128 = edge)</returns>
    public static byte[] GenerateSdf(
        byte[] bitmap,
        int bmpWidth, int bmpHeight,
        int padding,
        int upsampling,
        out int sdfWidth, out int sdfHeight)
    {
        int padHigh = padding * upsampling;
        int paddedW = bmpWidth + 2 * padHigh;
        int paddedH = bmpHeight + 2 * padHigh;

        var inside = new float[paddedW * paddedH];
        var outside = new float[paddedW * paddedH];

        if (upsampling == 1)
            InitializeFromAntialiased(bitmap, bmpWidth, bmpHeight, padHigh, paddedW, inside, outside);
        else
            InitializeFromBinary(bitmap, bmpWidth, bmpHeight, padHigh, paddedW, inside, outside);

        ComputeEdt(inside, paddedW, paddedH);
        ComputeEdt(outside, paddedW, paddedH);

        // Merge: signed distance = sqrt(outside) - sqrt(inside)
        var sdfHigh = new float[paddedW * paddedH];
        for (int i = 0; i < sdfHigh.Length; i++)
            sdfHigh[i] = MathF.Sqrt(outside[i]) - MathF.Sqrt(inside[i]);

        float[] sdfFinal;
        if (upsampling > 1)
        {
            sdfWidth = (int)Math.Ceiling(bmpWidth / (double)upsampling) + 2 * padding;
            sdfHeight = (int)Math.Ceiling(bmpHeight / (double)upsampling) + 2 * padding;
            sdfFinal = BilinearDownsample(sdfHigh, paddedW, paddedH, sdfWidth, sdfHeight);
        }
        else
        {
            sdfWidth = paddedW;
            sdfHeight = paddedH;
            sdfFinal = sdfHigh;
        }

        // Normalize to 0-255 (128 = edge)
        var result = new byte[sdfWidth * sdfHeight];
        for (int i = 0; i < result.Length; i++)
        {
            float normalized = Math.Clamp(sdfFinal[i] / padding, -1f, 1f) * 127.5f + 128f;
            result[i] = (byte)Math.Clamp(MathF.Round(normalized), 0f, 255f);
        }

        return result;
    }

    /// <summary>SDFAA: sub-pixel distance seeding from antialiased values.</summary>
    private static void InitializeFromAntialiased(
        byte[] bitmap, int bmpW, int bmpH,
        int pad, int paddedW,
        float[] inside, float[] outside)
    {
        Array.Fill(inside, INF);
        Array.Fill(outside, INF);

        for (int y = 0; y < bmpH; y++)
        {
            for (int x = 0; x < bmpW; x++)
            {
                float v = bitmap[y * bmpW + x] / 255f;
                int pi = (y + pad) * paddedW + (x + pad);
                inside[pi] = v > 0 ? v * v : INF;
                outside[pi] = v < 1f ? (1f - v) * (1f - v) : INF;
            }
        }
    }

    /// <summary>SDF8/16/32: binary initialization from binarized bitmap.</summary>
    private static void InitializeFromBinary(
        byte[] bitmap, int bmpW, int bmpH,
        int pad, int paddedW,
        float[] inside, float[] outside)
    {
        Array.Fill(inside, INF);
        Array.Fill(outside, INF);

        for (int y = 0; y < bmpH; y++)
        {
            for (int x = 0; x < bmpW; x++)
            {
                int pi = (y + pad) * paddedW + (x + pad);
                if (bitmap[y * bmpW + x] >= 128)
                    inside[pi] = 0;
                else
                    outside[pi] = 0;
            }
        }
    }

    /// <summary>2D squared Euclidean distance transform (Felzenszwalb parabola envelope).</summary>
    private static void ComputeEdt(float[] grid, int width, int height)
    {
        int maxDim = Math.Max(width, height);
        var f = new float[maxDim];
        var v = new int[maxDim];
        var z = new float[maxDim + 1];
        var d = new float[maxDim];

        // Rows
        for (int y = 0; y < height; y++)
        {
            int offset = y * width;
            for (int x = 0; x < width; x++)
                f[x] = grid[offset + x];

            Edt1D(f, width, v, z, d);

            for (int x = 0; x < width; x++)
                grid[offset + x] = d[x];
        }

        // Columns
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
                f[y] = grid[y * width + x];

            Edt1D(f, height, v, z, d);

            for (int y = 0; y < height; y++)
                grid[y * width + x] = d[y];
        }
    }

    /// <summary>1D squared distance transform using lower envelope of parabolas.</summary>
    private static void Edt1D(float[] f, int n, int[] v, float[] z, float[] d)
    {
        int k = 0;
        v[0] = 0;
        z[0] = -INF;
        z[1] = INF;

        for (int q = 1; q < n; q++)
        {
            float s;
            while (true)
            {
                s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                if (s > z[k]) break;
                k--;
            }

            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = INF;
        }

        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q)
                k++;
            float dx = q - v[k];
            d[q] = dx * dx + f[v[k]];
        }
    }

    /// <summary>Bilinear downsample in SDF domain.</summary>
    private static float[] BilinearDownsample(float[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new float[dstW * dstH];
        float scaleX = (float)srcW / dstW;
        float scaleY = (float)srcH / dstH;

        for (int dy = 0; dy < dstH; dy++)
        {
            for (int dx = 0; dx < dstW; dx++)
            {
                float sx = (dx + 0.5f) * scaleX - 0.5f;
                float sy = (dy + 0.5f) * scaleY - 0.5f;

                int x0 = Math.Max(0, (int)MathF.Floor(sx));
                int y0 = Math.Max(0, (int)MathF.Floor(sy));
                int x1 = Math.Min(srcW - 1, x0 + 1);
                int y1 = Math.Min(srcH - 1, y0 + 1);

                float fx = sx - x0;
                float fy = sy - y0;

                dst[dy * dstW + dx] =
                    src[y0 * srcW + x0] * (1 - fx) * (1 - fy) +
                    src[y0 * srcW + x1] * fx * (1 - fy) +
                    src[y1 * srcW + x0] * (1 - fx) * fy +
                    src[y1 * srcW + x1] * fx * fy;
            }
        }

        return dst;
    }
}
