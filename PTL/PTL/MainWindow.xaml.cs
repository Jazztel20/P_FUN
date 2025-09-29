using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

// ScottPlot v5.x
using ScottPlot;

namespace PTL
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, List<TimePoint>> _series = new();

        public MainWindow()
        {
            InitializeComponent();

            // (Optionnel) Données de démo pour voir un graphe sans CSV
            var years = Enumerable.Range(1875, DateTime.Now.Year - 1875 + 1).Select(y => new DateTime(y, 1, 1));
            _series["Lausanne"] = years.Select(d => new TimePoint(d, 1100 + (d.Year % 23 - 11) * 20)).ToList();
            _series["Lugano"] = years.Select(d => new TimePoint(d, 1200 + (d.Year % 19 - 9) * 25)).ToList();
            _series["Zürich"] = years.Select(d => new TimePoint(d, 1000 + (d.Year % 17 - 8) * 18)).ToList();
            _series["Davos"] = years.Select(d => new TimePoint(d, 900 + (d.Year % 13 - 6) * 22)).ToList();

            var all = _series.AllPoints();
            dpFrom.SelectedDate = all.MinDate();
            dpTo.SelectedDate = all.MaxDate();

            this.Loaded += OnWindowLoaded;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            Redraw();
        }

        // === UI events ===
        private void OnImportCsv(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "CSV/TXT (*.csv;*.txt)|*.csv;*.txt|Tous les fichiers (*.*)|*.*" };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                // Importeur adapté à ton CSV: Ville | Date et heure | Précipitations (mm) | Jours de précipitations
                var (name, points) = MeteoSuisseCsvImporter.LoadSeries(dlg.FileName);
                _series[name] = points.ToList();
                txtStatus.Text = "Fichier chargé avec succès !";

                var all = _series.AllPoints();
                dpFrom.SelectedDate = all.MinDate();
                dpTo.SelectedDate = all.MaxDate();

                Redraw();
            }
            catch (FormatException)
            {
                txtStatus.Text = "Fichier invalide. Vérifiez les colonnes et le séparateur.";
            }
            catch (ArgumentOutOfRangeException)
            {
                txtStatus.Text = "Date invalide. Veuillez réessayer.";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Erreur: {ex.Message}";
            }
        }

        private void OnReset(object sender, RoutedEventArgs e)
        {
            var all = _series.AllPoints();
            dpFrom.SelectedDate = all.MinDate();
            dpTo.SelectedDate = all.MaxDate();
            cbFunction.SelectedIndex = 0;
            txtStatus.Text = string.Empty;
            Redraw();
        }

        private void OnCityToggle(object sender, RoutedEventArgs e) => Redraw();
        private void OnDateChanged(object sender, SelectionChangedEventArgs e) => Redraw();
        private void OnFunctionChanged(object sender, SelectionChangedEventArgs e) => Redraw();

        // === Rendu ===
        private void Redraw()
        {
            if (Plot is null || Plot.Plot is null)
                return; // le contrôle n’est pas encore prêt

            var plt = Plot.Plot;      // ScottPlot v5: accès au Plot interne
            plt.Clear();

            var from = dpFrom.SelectedDate ?? DateTime.MinValue;
            var to = dpTo.SelectedDate ?? DateTime.MaxValue;
            if (from > to) (from, to) = (to, from);

            var selected = new[]
            {
                ("Lausanne", cbLausanne.IsChecked == true),
                ("Lugano",   cbLugano.IsChecked   == true),
                ("Zürich",   cbZurich.IsChecked   == true),
                ("Davos",    cbDavos.IsChecked    == true),
            }
            .Where(t => t.Item2)
            .Select(t => t.Item1)
            .ToList();

            // Séries cochées → courbes
            _series
                .Where(kv => selected.Contains(kv.Key))
                .Select(kv => new { kv.Key, Points = kv.Value.Between(from, to) })
                .ToList()
                .ForEach(s =>
                {
                    var xs = s.Points.Select(p => p.Timestamp.ToOADate()).ToArray();
                    var ys = s.Points.Select(p => p.Value ?? double.NaN).ToArray();

                    // v5: pas de paramètre nommé 'label' → on assigne après
                    var sc = plt.Add.Scatter(xs, ys);
                    sc.LegendText = s.Key;
                });

            // Mode Fonctions (v5)
            var sel = (cbFunction.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(sel) && sel != "(aucune)")
            {
                var (fx, domain) = FunctionFactory.FromLabel(sel!);
                if (fx != null)
                {
                    var xs = domain.Sample(400).ToArray();
                    var ys = xs.Select(fx).ToArray();
                    var scFx = plt.Add.Scatter(xs, ys);
                    scFx.LegendText = sel;
                }
            }

            // Légende et axes (v5)
            plt.Legend.IsVisible = true;
            plt.Axes.DateTimeTicksBottom();
            plt.Axes.Left.Label.Text = "Précipitations en mm";
            plt.Axes.Bottom.Label.Text = "Années";

            Plot.Refresh();
        }
    }

    // === Modèle ===
    public record TimePoint(DateTime Timestamp, double? Value);

    // === Extensions LINQ (aucun 'for') ===
    public static class LinqTimeExtensions
    {
        public static IEnumerable<TimePoint> Between(this IEnumerable<TimePoint> src, DateTime from, DateTime to)
            => src.Where(p => p.Timestamp >= from && p.Timestamp <= to);

        public static IEnumerable<TimePoint> AllPoints(this IDictionary<string, List<TimePoint>> map)
            => map.SelectMany(kv => kv.Value);

        public static DateTime MinDate(this IEnumerable<TimePoint> pts) => pts.Min(p => p.Timestamp);
        public static DateTime MaxDate(this IEnumerable<TimePoint> pts) => pts.Max(p => p.Timestamp);

        public static IEnumerable<double> Sample(this (double a, double b) dom, int n)
            => Enumerable.Range(0, Math.Max(2, n))
                         .Select(i => dom.a + (dom.b - dom.a) * i / (Math.Max(2, n) - 1.0));
    }

    // === Fonctions prédéfinies ===
    public static class FunctionFactory
    {
        public static (Func<double, double>? fx, (double a, double b) domain) FromLabel(string label) => label switch
        {
            "x^2" => (x => x * x, (-10, 10)),
            "sin(x)" => (Math.Sin, (-10, 10)),
            "sin(x)+sin(3x)/3+sin(5x)/5" => (x => Math.Sin(x) + Math.Sin(3 * x) / 3 + Math.Sin(5 * x) / 5, (-10, 10)),
            "x*sin(x)" => (x => x * Math.Sin(x), (-10, 10)),
            _ => (null, (0, 1)),
        };
    }
}
