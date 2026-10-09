using System.Text;

namespace Smt.Core;

/// <summary>Portable polling reader. Handles UTF-8/UTF-16, split writes, new files, and truncation.</summary>
public sealed class LogTailer
{
    private sealed class Cursor
    {
        public long Position;
        public Decoder? Decoder;
        public string Pending = "";
        public byte[] Checkpoint = [];
    }
    private readonly Dictionary<string, Cursor> cursors = new();
    public string Folder { get; }
    public int FilesFound { get; private set; }
    public int EligibleFiles { get; private set; }
    public int FilesRead { get; private set; }
    public long BytesRead { get; private set; }
    public List<string> ReadErrors { get; } = [];
    public LogTailer(string folder) => Folder = folder;
    public IReadOnlyList<(string Source, string Line)> Poll()
    {
        ReadErrors.Clear(); FilesFound=EligibleFiles=FilesRead=0; BytesRead=0;
        var result = new List<(string, string)>();
        var allPaths=Directory.EnumerateFiles(Folder, "*.txt").ToArray();FilesFound=allPaths.Length;
        var paths = allPaths.Where(p => File.GetLastWriteTimeUtc(p) > DateTime.UtcNow.AddDays(-2)).ToHashSet();
        EligibleFiles=paths.Count;
        foreach (var stale in cursors.Keys.Where(p => !paths.Contains(p)).ToArray()) cursors.Remove(stale);
        foreach (var path in paths)
        {
            try
            {
            var info = new FileInfo(path);
            if (!cursors.TryGetValue(path, out var cursor) || info.Length < cursor.Position)
                cursors[path] = cursor = new Cursor();
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if(cursor.Checkpoint.Length > 0)
            {
                file.Position = cursor.Position - cursor.Checkpoint.Length;
                var prior = new byte[cursor.Checkpoint.Length];
                file.ReadExactly(prior);
                if(!prior.SequenceEqual(cursor.Checkpoint)) cursors[path] = cursor = new Cursor();
            }
            file.Position = 0;
            if (cursor.Decoder == null)
            {
                if (file.Length < 3) continue; // Wait for a complete encoding marker.
                var bom = new byte[3]; file.ReadExactly(bom);
                var encoding = bom[0] == 255 && bom[1] == 254 ? Encoding.Unicode : bom[0] == 254 && bom[1] == 255 ? Encoding.BigEndianUnicode : Encoding.UTF8;
                cursor.Position = encoding == Encoding.UTF8 ? (bom.SequenceEqual(new byte[] {239,187,191}) ? 3 : 0) : 2;
                cursor.Decoder = encoding.GetDecoder();
                // Bound first-load work; skip a partial opening line in long historical files.
                if (file.Length - cursor.Position > 1024 * 1024)
                {
                    cursor.Position = file.Length - 1024 * 1024;
                    if (encoding != Encoding.UTF8 && cursor.Position % 2 != 0) cursor.Position++;
                    cursor.Pending = "[skipped historical prefix] ";
                }
            }
            file.Position = cursor.Position;
            int remaining = (int)Math.Min(file.Length - cursor.Position, 1024 * 1024);
            var buffer = new byte[remaining]; int read = file.Read(buffer, 0, remaining);
            cursor.Position += read; FilesRead++; BytesRead+=read;
            cursor.Checkpoint = new byte[(int)Math.Min(64,cursor.Position)];
            file.Position = cursor.Position - cursor.Checkpoint.Length;
            file.ReadExactly(cursor.Checkpoint);
            var chars = new char[Encoding.UTF8.GetMaxCharCount(read) + 4];
            int count = cursor.Decoder.GetChars(buffer, 0, read, chars, 0, false);
            var text = cursor.Pending + new string(chars, 0, count);
            int last = text.LastIndexOf('\n');
            if (last < 0) { cursor.Pending = text.Length > 16384 ? "" : text; continue; }
            foreach (var line in text[..last].Split('\n')) result.Add((Path.GetFileNameWithoutExtension(path), line.TrimEnd('\r')));
            cursor.Pending = text[(last + 1)..];
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException)
            { ReadErrors.Add(Path.GetFileName(path)); cursors.Remove(path); }
        }
        return result;
    }
}
