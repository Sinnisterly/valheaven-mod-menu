using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ValheimAdminMenu
{
    internal static class BlueprintBbp
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private const int HeaderSize = 48;
        private const int MaxRawBytes = 64 * 1024 * 1024;

        internal static bool LooksLikeBbp(string path, byte[] bytes)
        {
            if (path != null && path.EndsWith(".bbp", StringComparison.OrdinalIgnoreCase)) return true;
            return bytes != null && bytes.Length >= 4
                && bytes[0] == (byte)'B' && bytes[1] == (byte)'B' && bytes[2] == (byte)'P' && bytes[3] == (byte)'F';
        }

        internal static BlueprintRecord Parse(byte[] fileBytes, string defaultName)
        {
            if (fileBytes == null || fileBytes.Length < HeaderSize)
                throw new InvalidOperationException("BBP file is too small.");
            if (fileBytes[0] != (byte)'B' || fileBytes[1] != (byte)'B' || fileBytes[2] != (byte)'P' || fileBytes[3] != (byte)'F')
                throw new InvalidOperationException("Not a BBPF blueprint file.");

            int version = BitConverter.ToInt32(fileBytes, 4);
            int rawLen = BitConverter.ToInt32(fileBytes, 8);
            int compressedLen = BitConverter.ToInt32(fileBytes, 12);
            if (version < 1 || version > 8)
                throw new InvalidOperationException("Unsupported BBP container version " + version + ".");
            if (rawLen <= 0 || rawLen > MaxRawBytes)
                throw new InvalidOperationException("BBP raw size is invalid.");
            if (compressedLen <= 0 || HeaderSize + compressedLen > fileBytes.Length)
                throw new InvalidOperationException("BBP compressed size is invalid.");

            var expectedHash = new byte[32];
            Buffer.BlockCopy(fileBytes, 16, expectedHash, 0, 32);
            using (var sha = SHA256.Create())
            {
                var actual = sha.ComputeHash(fileBytes, HeaderSize, compressedLen);
                for (int i = 0; i < 32; i++)
                    if (actual[i] != expectedHash[i])
                        throw new InvalidOperationException("BBP checksum mismatch.");
            }

            byte[] raw;
            using (var input = new MemoryStream(fileBytes, HeaderSize, compressedLen, writable: false))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream(rawLen))
            {
                deflate.CopyTo(output);
                raw = output.ToArray();
            }
            if (raw.Length != rawLen)
                throw new InvalidOperationException("BBP decompressed size mismatch.");

            return DecodePayload(raw, defaultName);
        }

        private static BlueprintRecord DecodePayload(byte[] raw, string defaultName)
        {
            using (var ms = new MemoryStream(raw, writable: false))
            using (var br = new BinaryReader(ms, Utf8))
            {
                int formatVersion = br.ReadInt32();
                if (formatVersion < 1 || formatVersion > 8)
                    throw new InvalidOperationException("Unsupported BBP format version " + formatVersion + ".");
                string name = br.ReadString();
                if (string.IsNullOrWhiteSpace(name)) name = defaultName ?? "blueprint";

                int prefabCount = br.ReadInt32();
                if (prefabCount < 0 || prefabCount > 250000)
                    throw new InvalidOperationException("Invalid BBP prefab table size.");
                var prefabs = new string[prefabCount];
                for (int i = 0; i < prefabCount; i++)
                    prefabs[i] = br.ReadString();

                int pieceCount = br.ReadInt32();
                if (pieceCount < 0 || pieceCount > Release250.MaxClipboardPieces)
                    throw new InvalidOperationException("BBP piece count exceeds limit (" + Release250.MaxClipboardPieces + ").");
                var pieces = new List<BlueprintPieceDto>(pieceCount);
                for (int i = 0; i < pieceCount; i++)
                {
                    int id = br.ReadInt32();
                    float x = br.ReadSingle(), y = br.ReadSingle(), z = br.ReadSingle();
                    float qx = br.ReadSingle(), qy = br.ReadSingle(), qz = br.ReadSingle(), qw = br.ReadSingle();
                    if (id < 0 || id >= prefabs.Length || string.IsNullOrWhiteSpace(prefabs[id])) continue;
                    pieces.Add(new BlueprintPieceDto
                    {
                        prefab = prefabs[id],
                        x = x, y = y, z = z,
                        qx = qx, qy = qy, qz = qz, qw = qw
                    });
                }

                BlueprintTerrainDto terrain = null;
                if (br.ReadBoolean())
                {
                    terrain = new BlueprintTerrainDto
                    {
                        version = br.ReadInt32(),
                        width = br.ReadInt32(),
                        spacing = br.ReadSingle(),
                        radius = br.ReadSingle(),
                        originX = br.ReadSingle(),
                        originZ = br.ReadSingle()
                    };
                    int cells = terrain.width * terrain.width;
                    if (terrain.width < 2 || cells <= 0 || cells > 449 * 449)
                        throw new InvalidOperationException("Invalid BBP terrain grid.");
                    terrain.heights = new float[cells];
                    for (int i = 0; i < cells; i++)
                        terrain.heights[i] = br.ReadSingle();
                }

                if (formatVersion >= 2 && ms.Position < ms.Length)
                {
                    int n = br.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        br.ReadInt32();
                        br.ReadString();
                        br.ReadInt32();
                        br.ReadInt32();
                    }
                }
                if (formatVersion >= 3 && ms.Position < ms.Length)
                {
                    int n = br.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        br.ReadInt32();
                        br.ReadString();
                    }
                }
                if (formatVersion >= 4 && ms.Position < ms.Length)
                {
                    int n = br.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        int index = br.ReadInt32();
                        float sx = br.ReadSingle(), sy = br.ReadSingle(), sz = br.ReadSingle();
                        if (index >= 0 && index < pieces.Count)
                        {
                            pieces[index].sx = sx == 0 ? 1f : sx;
                            pieces[index].sy = sy == 0 ? 1f : sy;
                            pieces[index].sz = sz == 0 ? 1f : sz;
                        }
                    }
                }

                if (pieces.Count == 0)
                    throw new InvalidOperationException("BBP contained no valid pieces.");

                return new BlueprintRecord
                {
                    name = name.Trim(),
                    savedUtc = DateTime.UtcNow.ToString("o"),
                    pieceCount = pieces.Count,
                    pieces = pieces.ToArray(),
                    terrain = terrain
                };
            }
        }
    }
}
