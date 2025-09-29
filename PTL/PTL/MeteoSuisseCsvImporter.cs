///
/// 
///

using System.Text;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PTL
{
    public static class MeteoSuisseCsvImporter
    {
        // Colonnes acceptées (insensible aux accents/majuscules):
        // "Ville" | "Date et heure" | "Précipitations (mm)" | "Jours de précipitations"
        public static (string Name, IEnumerable<TimePoint> Points) LoadSeries(string path)
        {
            var text = File.ReadAllText(path, Encoding.Latin1);

            // Détecter séparateur ; , ou tabulation
            var sample = new string(text.Take(2048).ToArray());
            var delim = new[] { ';', ',', '\t' }
                .OrderByDescending(ch => sample.Count(c => c == ch))
                .FirstOrDefault();
            if (delim == default) throw new FormatException("Séparateur inconnu.");

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                            .Where(l => !string.IsNullOrWhiteSpace(l));

            var header = lines.First();
            var headers = header.Split(delim).Select(Norm).ToArray();

            int idxVille = IndexOf(headers, "ville");
            int idxDate = IndexOf(headers, "date et heure");
            int idxPrec = IndexOf(headers, "precipitations (mm)");
            if (idxDate < 0 || idxPrec < 0)
                throw new FormatException("Colonnes obligatoires manquantes (Date et/ou Précipitations).");

            var frCH = CultureInfo.GetCultureInfo("fr-CH");

            // Parse lignes → TimePoint (LINQ only)
            var rows = lines.Skip(1).Select(l => l.Split(delim));

            var points = rows
                .Where(parts => parts.Length > Math.Max(idxDate, idxPrec))
                .Select(parts => new
                {
                    City = idxVille >= 0 ? parts[idxVille]?.Trim() : "",
                    Date = ParseDate(parts[idxDate]),
                    Val = ParseDoubleFlexible(parts[idxPrec], frCH)
                })
                .Where(x => x.Date != null) // garde seulement les lignes avec date valide
                .Select(x => new TimePoint(x.Date!.Value, x.Val))
                .OrderBy(p => p.Timestamp);

            // Nom de série = ville (si trouvée) sinon nom de fichier
            var anyCity = rows
                .Where(p => idxVille >= 0 && p.Length > idxVille)
                .Select(p => p[idxVille]?.Trim())
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

            var name = GuessCityName(anyCity);
            if (string.IsNullOrWhiteSpace(name))
                name = Path.GetFileNameWithoutExtension(path);

            return (name, points);
        }

        // --- helpers ---

        private static int IndexOf(string[] headers, string target)
            => headers.Select((h, i) => new { h, i })
                      .Where(x => x.h == Norm(target))
                      .Select(x => x.i)
                      .DefaultIfEmpty(-1)
                      .First();

        private static string Norm(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var formD = s.Trim().ToLowerInvariant()
                .Normalize(System.Text.NormalizationForm.FormD);
            var noDiacritics = new string(formD.Where(ch =>
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) !=
                System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
            return noDiacritics.Normalize(System.Text.NormalizationForm.FormC);
        }

        private static DateTime? ParseDate(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var str = s.Trim();

            var formats = new[]
            {
                "dd.MM.yyyy HH:mm", // "01.01.1875 00:00"
                "dd.MM.yyyy",
                "yyyy-MM-dd",
                "yyyy"
            };

            if (DateTime.TryParseExact(str, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
                return dt;

            return DateTime.TryParse(str, CultureInfo.GetCultureInfo("fr-CH"),
                DateTimeStyles.AllowWhiteSpaces, out dt) ? dt : (DateTime?)null;
        }

        private static double? ParseDoubleFlexible(string? s, CultureInfo frCH)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var raw = s.Trim();

            return double.TryParse(raw, NumberStyles.Any, frCH, out var v) ? v
                 : double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v
                 : (double?)null;
        }

        private static string GuessCityName(string? codeOrName)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LSN"] = "Lausanne",
                ["LUG"] = "Lugano",
                ["ZRH"] = "Zürich",
                ["DAV"] = "Davos",
                ["DVS"] = "Davos"
            };

            if (string.IsNullOrWhiteSpace(codeOrName)) return "";
            var key = codeOrName.Trim();

            // code connu → nom
            if (map.TryGetValue(key, out var city)) return city;

            // sinon retourne tel quel (si déjà le nom complet)
            return key;
        }
    }
}
