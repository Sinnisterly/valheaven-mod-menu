using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ValheimAdminMenu
{
    [Serializable]
    internal sealed class BlueprintLibrary
    {
        public int version = 1;
        public List<BlueprintRecord> blueprints = new List<BlueprintRecord>();
    }

    [Serializable]
    internal sealed class BlueprintRecord
    {
        public string name = "";
        public string savedUtc = "";
        public int pieceCount;
        public BlueprintPieceDto[] pieces = Array.Empty<BlueprintPieceDto>();
        public BlueprintTerrainDto terrain;
    }

    [Serializable]
    internal sealed class BlueprintPieceDto
    {
        public string prefab = "";
        public float x, y, z;
        public float qx, qy, qz, qw = 1f;
        public float sx = 1f, sy = 1f, sz = 1f;
    }

    [Serializable]
    internal sealed class BlueprintTerrainDto
    {
        public int version = 1;
        public int width;
        public float spacing = 1f;
        public float radius;
        public float originX;
        public float originZ;
        public float[] heights;
    }

    /// <summary>Named blueprints (prefab + local pose). Library JSON is global; shareable .vbp files are self-contained under BlueprintsDir.</summary>
    internal static class BlueprintStore
    {
        private static BlueprintLibrary store;
        private const int MaxNameLen = 64;
        private const int MaxBlueprints = 100;
        private const int MaxPiecesPerBlueprint = Release250.MaxClipboardPieces;
        private const long MaxImportBytes = 32L * 1024L * 1024L;

        internal static string LibraryPath => AppPaths.BlueprintsPath;
        internal static string ExportDir => AppPaths.BlueprintsDir;

        private static void Ensure()
        {
            if (store != null) return;
            store = new BlueprintLibrary();
            try
            {
                if (File.Exists(LibraryPath))
                    store = BlueprintJson.ParseLibrary(File.ReadAllText(LibraryPath));
            }
            catch { store = new BlueprintLibrary(); }
            if (store.blueprints == null) store.blueprints = new List<BlueprintRecord>();
            foreach (var bp in store.blueprints)
                NormalizePieces(bp);
            store.version = Math.Max(1, store.version);
        }

        private static void SaveDisk()
        {
            Ensure();
            foreach (var bp in store.blueprints)
                NormalizePieces(bp);
            string directory = Path.GetDirectoryName(LibraryPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = LibraryPath + ".tmp";
            File.WriteAllText(temp, BlueprintJson.WriteLibrary(store), Encoding.UTF8);
            if (File.Exists(LibraryPath)) File.Replace(temp, LibraryPath, LibraryPath + ".bak");
            else File.Move(temp, LibraryPath);
        }

        internal static IEnumerable<BlueprintRecord> List()
        {
            Ensure();
            return store.blueprints
                .Where(b => b != null && !string.IsNullOrWhiteSpace(b.name))
                .OrderBy(b => b.name, StringComparer.OrdinalIgnoreCase);
        }

        internal static BlueprintRecord Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            Ensure();
            return store.blueprints.FirstOrDefault(b =>
                b != null && string.Equals(b.name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        internal static void SaveFromClipboard(string name)
        {
            if (Release250.Clipboard == null || Release250.Clipboard.Count == 0)
                throw new InvalidOperationException("Clipboard is empty. Copy a volume or load a blueprint first.");
            name = SanitizeName(name);
            if (Release250.Clipboard.Count > MaxPiecesPerBlueprint)
                throw new InvalidOperationException("Clipboard has too many pieces (max " + MaxPiecesPerBlueprint + "). Shrink the selection.");

            Ensure();
            var record = Find(name);
            if (record == null)
            {
                if (store.blueprints.Count >= MaxBlueprints)
                    throw new InvalidOperationException("Blueprint library is full (" + MaxBlueprints + "). Delete one first.");
                record = new BlueprintRecord { name = name };
                store.blueprints.Add(record);
            }
            record.name = name;
            record.savedUtc = DateTime.UtcNow.ToString("o");
            record.pieces = Release250.Clipboard.Select(ToDto).Where(p => p != null).ToArray();
            record.pieceCount = record.pieces.Length;
            record.terrain = CloneTerrain(Release250.ClipboardTerrain);
            if (record.pieceCount == 0)
                throw new InvalidOperationException("Clipboard had no valid pieces to save.");
            SaveDisk();
            Plugin.Instance.Report("Saved \"" + name + "\" (" + record.pieceCount + " pieces).");
        }

        internal static void LoadToClipboard(string name)
        {
            var record = Find(name);
            if (record == null) throw new ArgumentException("Choose a blueprint.");
            NormalizePieces(record);
            if (record.pieces == null || record.pieces.Length == 0)
                throw new InvalidOperationException("Blueprint \"" + record.name + "\" has no pieces. Re-copy and save it again.");

            Release250.CancelPastePreview(silent: true);
            Release250.Clipboard.Clear();
            foreach (var dto in record.pieces)
            {
                var piece = FromDto(dto);
                if (piece != null) Release250.Clipboard.Add(piece);
            }
            Release250.ClipboardTerrain = CloneTerrain(record.terrain);
            Plugin.Instance.Report("Loaded \"" + record.name + "\" (" + Release250.Clipboard.Count + " pieces).");
            Release250.MaybeAutoPastePreview();
        }

        internal static void Rename(string fromName, string toName)
        {
            var record = Find(fromName);
            if (record == null) throw new ArgumentException("Choose a blueprint to rename.");
            toName = SanitizeName(toName);
            var clash = Find(toName);
            if (clash != null && !ReferenceEquals(clash, record))
                throw new InvalidOperationException("A blueprint named \"" + toName + "\" already exists.");
            string old = record.name;
            record.name = toName;
            SaveDisk();
            Plugin.Instance.Report("Renamed \"" + old + "\" to \"" + toName + "\".");
        }

        internal static void Delete(string name)
        {
            Ensure();
            int n = store.blueprints.RemoveAll(b =>
                b != null && string.Equals(b.name, (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (n <= 0) throw new ArgumentException("Blueprint not found.");
            SaveDisk();
            Plugin.Instance.Report("Deleted \"" + (name ?? "").Trim() + "\".");
        }

        /// <summary>Write a self-contained .vbp (name + full pieces[]).</summary>
        internal static string Export(string name)
        {
            var record = Find(name);
            if (record == null) throw new ArgumentException("Choose a blueprint to export.");
            NormalizePieces(record);
            if (record.pieces == null || record.pieces.Length == 0)
                throw new InvalidOperationException(
                    "\"" + record.name + "\" has no pieces to export. Load or re-save it, then export again.");

            Directory.CreateDirectory(ExportDir);
            string fileName = FileNameFor(record.name) + ".vbp";
            string file = Path.Combine(ExportDir, fileName);
            // Fresh copy so we never share a live library array reference.
            var payload = new BlueprintRecord
            {
                name = record.name,
                savedUtc = string.IsNullOrEmpty(record.savedUtc) ? DateTime.UtcNow.ToString("o") : record.savedUtc,
                pieceCount = record.pieces.Length,
                pieces = ClonePieces(record.pieces),
                terrain = CloneTerrain(record.terrain)
            };
            string json = BlueprintJson.WriteRecord(payload);
            if (string.IsNullOrEmpty(json) || json.IndexOf("\"pieces\"", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Export failed to write piece data. Try saving the blueprint again.");
            string temp = file + ".tmp";
            File.WriteAllText(temp, json, Encoding.UTF8);
            if (File.Exists(file)) File.Replace(temp, file, file + ".bak");
            else File.Move(temp, file);
            // Filename only; full path was truncating the footer.
            Plugin.Instance.Report("Exported \"" + record.name + "\" (" + payload.pieceCount + " pieces) as " + fileName + ".");
            return file;
        }

        internal static void ImportFile(string fileNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrPath))
                throw new ArgumentException("Enter a .vbp / .bbp / .blueprint file name or path.");
            string path = ResolveImportPath(fileNameOrPath.Trim());
            if (!File.Exists(path))
                throw new FileNotFoundException("Blueprint file not found: " + Path.GetFileName(path));
            if (new FileInfo(path).Length > MaxImportBytes)
                throw new InvalidOperationException("Import file exceeds " + (MaxImportBytes / (1024 * 1024)) + " MB.");

            var bytes = File.ReadAllBytes(path);
            if (BlueprintBbp.LooksLikeBbp(path, bytes))
            {
                var bbp = BlueprintBbp.Parse(bytes, Path.GetFileNameWithoutExtension(path));
                UpsertImported(bbp);
                SaveDisk();
                string terrainNote = bbp.terrain != null ? " + terrain" : "";
                Plugin.Instance.Report("Imported BBP \"" + bbp.name + "\" (" + bbp.pieceCount + " pieces" + terrainNote + ").");
                return;
            }

            string content = Encoding.UTF8.GetString(bytes);

            if (content.StartsWith("#Name:") || path.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase))
            {
                var planBuildRecord = ParsePlanBuild(content, Path.GetFileNameWithoutExtension(path));
                if (planBuildRecord != null && HasPieces(planBuildRecord))
                {
                    UpsertImported(planBuildRecord);
                    SaveDisk();
                    Plugin.Instance.Report("Imported PlanBuild blueprint \"" + planBuildRecord.name + "\" (" + planBuildRecord.pieceCount + " pieces).");
                    return;
                }
            }

            var single = BlueprintJson.ParseRecord(content);
            NormalizePieces(single);
            if (single != null && HasPieces(single) && !string.IsNullOrWhiteSpace(single.name))
            {
                UpsertImported(single);
                SaveDisk();
                Plugin.Instance.Report("Imported \"" + single.name + "\" (" + single.pieceCount + " pieces).");
                return;
            }
            if (single != null && !string.IsNullOrWhiteSpace(single.name) && !HasPieces(single)
                && content.IndexOf("\"pieces\"", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException(
                    "\"" + single.name + "\" is an incomplete .vbp (no pieces). Re-export with this Valheaven build.");

            var lib = BlueprintJson.ParseLibrary(content);
            if (lib?.blueprints != null && lib.blueprints.Count > 0)
            {
                int n = 0;
                foreach (var bp in lib.blueprints)
                {
                    NormalizePieces(bp);
                    if (bp == null || !HasPieces(bp) || string.IsNullOrWhiteSpace(bp.name)) continue;
                    UpsertImported(bp);
                    n++;
                }
                if (n == 0) throw new InvalidOperationException("No valid blueprints with pieces in that file.");
                SaveDisk();
                Plugin.Instance.Report("Imported " + n + " blueprint(s).");
                return;
            }
            throw new InvalidOperationException("Unrecognized blueprint file format.");
        }

        private static BlueprintRecord ParsePlanBuild(string content, string defaultName)
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string name = defaultName;
            bool piecesSection = false;
            var pieces = new List<BlueprintPieceDto>();

            foreach (var line in lines)
            {
                if (line.StartsWith("#Name:", StringComparison.OrdinalIgnoreCase))
                {
                    name = line.Substring(6).Trim();
                    continue;
                }
                if (line.StartsWith("#Pieces", StringComparison.OrdinalIgnoreCase))
                {
                    piecesSection = true;
                    continue;
                }
                if (piecesSection && !line.StartsWith("#"))
                {
                    var parts = line.Split(';');
                    if (parts.Length >= 9)
                    {
                        try
                        {
                            var dto = new BlueprintPieceDto
                            {
                                prefab = parts[0].Trim(),
                                x = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                                y = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
                                z = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
                                qx = float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture),
                                qy = float.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture),
                                qz = float.Parse(parts[7], System.Globalization.CultureInfo.InvariantCulture),
                                qw = float.Parse(parts[8], System.Globalization.CultureInfo.InvariantCulture),
                            };
                            if (parts.Length >= 13)
                            {
                                try
                                {
                                    // Info (parts[9]) can hold semicolons; scale is always the last 3 floats.
                                    dto.sx = float.Parse(parts[parts.Length - 3], System.Globalization.CultureInfo.InvariantCulture);
                                    dto.sy = float.Parse(parts[parts.Length - 2], System.Globalization.CultureInfo.InvariantCulture);
                                    dto.sz = float.Parse(parts[parts.Length - 1], System.Globalization.CultureInfo.InvariantCulture);
                                }
                                catch { } // default scale 1
                            }
                            pieces.Add(dto);
                        }
                        catch { }
                    }
                }
            }

            if (pieces.Count == 0) return null;

            return new BlueprintRecord
            {
                name = name,
                savedUtc = DateTime.UtcNow.ToString("o"),
                pieceCount = pieces.Count,
                pieces = pieces.ToArray()
            };
        }

        internal static IEnumerable<string> ListExportFiles()
        {
            try
            {
                if (!Directory.Exists(ExportDir)) return Array.Empty<string>();
                return Directory.GetFiles(ExportDir, "*.*")
                    .Where(f =>
                        f.EndsWith(".vbp", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".bbp", StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFileName)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Offline check that JSON round-trip keeps pieces[].</summary>
        internal static bool ValidateSerializationRoundTrip(int pieceCount = 48)
        {
            var pieces = new BlueprintPieceDto[pieceCount];
            for (int i = 0; i < pieceCount; i++)
            {
                pieces[i] = new BlueprintPieceDto
                {
                    prefab = "wood_wall_" + i,
                    x = i * 0.1f, y = 1f, z = -i * 0.05f,
                    qx = 0, qy = 0, qz = 0, qw = 1f,
                    sx = 1f, sy = 1f, sz = 1f
                };
            }
            var original = new BlueprintRecord
            {
                name = "roundtrip",
                savedUtc = "2026-01-01T00:00:00Z",
                pieceCount = pieceCount,
                pieces = pieces
            };
            string json = BlueprintJson.WriteRecord(original);
            if (json.IndexOf("\"pieces\"", StringComparison.OrdinalIgnoreCase) < 0) return false;
            var loaded = BlueprintJson.ParseRecord(json);
            NormalizePieces(loaded);
            if (loaded == null || loaded.pieces == null || loaded.pieces.Length != pieceCount) return false;
            if (!string.Equals(loaded.pieces[0].prefab, "wood_wall_0", StringComparison.Ordinal)) return false;
            if (!string.Equals(loaded.pieces[pieceCount - 1].prefab, "wood_wall_" + (pieceCount - 1), StringComparison.Ordinal))
                return false;
            return true;
        }

        private static void UpsertImported(BlueprintRecord incoming)
        {
            Ensure();
            string name = SanitizeName(incoming.name);
            NormalizePieces(incoming);
            if (incoming.pieces.Length > MaxPiecesPerBlueprint)
                throw new InvalidOperationException("Blueprint \"" + name + "\" exceeds max piece count.");
            var existing = Find(name);
            if (existing == null)
            {
                if (store.blueprints.Count >= MaxBlueprints)
                    throw new InvalidOperationException("Library full while importing. Delete some blueprints first.");
                existing = new BlueprintRecord { name = name };
                store.blueprints.Add(existing);
            }
            existing.name = name;
            existing.savedUtc = string.IsNullOrEmpty(incoming.savedUtc) ? DateTime.UtcNow.ToString("o") : incoming.savedUtc;
            existing.pieces = ClonePieces(incoming.pieces.Where(p => p != null && !string.IsNullOrWhiteSpace(p.prefab)).ToArray());
            existing.pieceCount = existing.pieces.Length;
            existing.terrain = CloneTerrain(incoming.terrain);
            if (existing.pieceCount == 0)
                throw new InvalidOperationException("Imported blueprint \"" + name + "\" had no valid pieces.");
        }

        private static void NormalizePieces(BlueprintRecord record)
        {
            if (record == null) return;
            if (record.pieces == null)
                record.pieces = Array.Empty<BlueprintPieceDto>();
            record.pieceCount = record.pieces.Length;
        }

        private static bool HasPieces(BlueprintRecord record) =>
            record != null && record.pieces != null && record.pieces.Length > 0;

        private static BlueprintPieceDto[] ClonePieces(BlueprintPieceDto[] source)
        {
            if (source == null || source.Length == 0) return Array.Empty<BlueprintPieceDto>();
            var copy = new BlueprintPieceDto[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var p = source[i];
                if (p == null) { copy[i] = new BlueprintPieceDto(); continue; }
                copy[i] = new BlueprintPieceDto
                {
                    prefab = p.prefab,
                    x = p.x, y = p.y, z = p.z,
                    qx = p.qx, qy = p.qy, qz = p.qz, qw = p.qw,
                    sx = p.sx, sy = p.sy, sz = p.sz
                };
            }
            return copy;
        }

        internal static BlueprintTerrainDto CloneTerrain(BlueprintTerrainDto source)
        {
            if (source == null || source.heights == null || source.heights.Length == 0) return null;
            var copy = new BlueprintTerrainDto
            {
                version = source.version,
                width = source.width,
                spacing = source.spacing,
                radius = source.radius,
                originX = source.originX,
                originZ = source.originZ,
                heights = new float[source.heights.Length]
            };
            Array.Copy(source.heights, copy.heights, source.heights.Length);
            return copy;
        }

        private static string ResolveImportPath(string input)
        {
            if (Path.IsPathRooted(input)) return Path.GetFullPath(input);
            string inExport = Path.Combine(ExportDir, input);
            if (!Path.HasExtension(inExport))
            {
                if (File.Exists(inExport + ".bbp")) return inExport + ".bbp";
                if (File.Exists(inExport + ".blueprint")) return inExport + ".blueprint";
                inExport += ".vbp";
            }
            if (File.Exists(inExport)) return inExport;
            string beside = Path.Combine(AppPaths.Root, input);
            if (File.Exists(beside)) return beside;
            return inExport;
        }

        private static string SanitizeName(string name)
        {
            name = Rules.Text(name, MaxNameLen).Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a blueprint name.");
            return name;
        }

        private static string FileNameFor(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ')
                    sb.Append(c == ' ' ? '_' : c);
            }
            string s = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(s) ? "blueprint" : s;
        }

        private static BlueprintPieceDto ToDto(Release250.CopiedPiece piece)
        {
            if (piece == null || string.IsNullOrWhiteSpace(piece.Prefab)) return null;
            var q = piece.LocalRot;
            var s = piece.Scale;
            var p = piece.LocalPos;
            return new BlueprintPieceDto
            {
                prefab = piece.Prefab,
                x = p.x, y = p.y, z = p.z,
                qx = q.x, qy = q.y, qz = q.z, qw = q.w,
                sx = s.x, sy = s.y, sz = s.z
            };
        }

        private static Release250.CopiedPiece FromDto(BlueprintPieceDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.prefab)) return null;
            var q = new Quaternion(dto.qx, dto.qy, dto.qz, dto.qw);
            if (q.x == 0 && q.y == 0 && q.z == 0 && q.w == 0) q = Quaternion.identity;
            else q.Normalize();
            var scale = new Vector3(
                dto.sx == 0 ? 1f : dto.sx,
                dto.sy == 0 ? 1f : dto.sy,
                dto.sz == 0 ? 1f : dto.sz);
            return new Release250.CopiedPiece
            {
                Prefab = dto.prefab.Trim(),
                LocalPos = new Vector3(dto.x, dto.y, dto.z),
                LocalRot = q,
                Scale = scale
            };
        }
    }
}
