using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.ComponentModel;

namespace SCCosmos
{
    public class FilaCiclo : INotifyPropertyChanged
    {
        private string _v1, _v2, _v3, _v4, _v5, _v6;
        private bool _l1, _l2, _l3, _l4, _l5, _l6;

        public int Orden { get; set; }

        public string Visita1 { get => _v1; set { _v1 = value; OnPropertyChanged(nameof(Visita1)); } }
        public string Visita2 { get => _v2; set { _v2 = value; OnPropertyChanged(nameof(Visita2)); } }
        public string Visita3 { get => _v3; set { _v3 = value; OnPropertyChanged(nameof(Visita3)); } }
        public string Visita4 { get => _v4; set { _v4 = value; OnPropertyChanged(nameof(Visita4)); } }
        public string Visita5 { get => _v5; set { _v5 = value; OnPropertyChanged(nameof(Visita5)); } }
        public string Visita6 { get => _v6; set { _v6 = value; OnPropertyChanged(nameof(Visita6)); } }

        public bool L1 { get => _l1; set { _l1 = value; OnPropertyChanged(nameof(L1)); } }
        public bool L2 { get => _l2; set { _l2 = value; OnPropertyChanged(nameof(L2)); } }
        public bool L3 { get => _l3; set { _l3 = value; OnPropertyChanged(nameof(L3)); } }
        public bool L4 { get => _l4; set { _l4 = value; OnPropertyChanged(nameof(L4)); } }
        public bool L5 { get => _l5; set { _l5 = value; OnPropertyChanged(nameof(L5)); } }
        public bool L6 { get => _l6; set { _l6 = value; OnPropertyChanged(nameof(L6)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class CicloVisitasWindow : Window
    {
        public ObservableCollection<string> ListaCongregaciones { get; set; }
        public ObservableCollection<FilaCiclo> FilasCiclo { get; set; }
        private bool _cargando = true;

        public CicloVisitasWindow()
        {
            InitializeComponent();
            DataContext = this;

            var congregacionesBD = DatabaseHelper.ObtenerCongregaciones().Select(c => c.Nombre).ToList();
            ListaCongregaciones = new ObservableCollection<string>(congregacionesBD);
            FilasCiclo = new ObservableCollection<FilaCiclo>();

            CargarMatrizDesdeDB();
            SincronizarHistorialSilencioso();

            TablaCiclo.ItemsSource = FilasCiclo;
            _cargando = false;
        }

        private void CargarMatrizDesdeDB()
        {
            string jsonGuardado = DatabaseHelper.ObtenerConfiguracion("MatrizCicloVisitas", "");

            if (!string.IsNullOrEmpty(jsonGuardado))
            {
                try
                {
                    var guardadas = JsonSerializer.Deserialize<List<FilaCiclo>>(jsonGuardado);
                    foreach (var f in guardadas)
                    {
                        if (f.L1 && string.IsNullOrEmpty(f.Visita1)) f.L1 = false;
                        if (f.L2 && string.IsNullOrEmpty(f.Visita2)) f.L2 = false;
                        if (f.L3 && string.IsNullOrEmpty(f.Visita3)) f.L3 = false;
                        if (f.L4 && string.IsNullOrEmpty(f.Visita4)) f.L4 = false;
                        if (f.L5 && string.IsNullOrEmpty(f.Visita5)) f.L5 = false;
                        if (f.L6 && string.IsNullOrEmpty(f.Visita6)) f.L6 = false;

                        f.PropertyChanged += Fila_PropertyChanged;
                        FilasCiclo.Add(f);
                    }
                }
                catch { }
            }

            if (FilasCiclo.Count == 0) GenerarEstructuraBase();
        }

        private void Fila_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_cargando) return;
            GuardarSilenciosoAsync();
        }

        private async void GuardarSilenciosoAsync()
        {
            try
            {
                string jsonAguardar = JsonSerializer.Serialize(FilasCiclo.ToList());
                DatabaseHelper.GuardarConfiguracion("MatrizCicloVisitas", jsonAguardar);

                TxtGuardado.Visibility = Visibility.Visible;
                await Task.Delay(2000);
                TxtGuardado.Visibility = Visibility.Hidden;
            }
            catch { }
        }

        private void SincronizarHistorialSilencioso()
        {
            if (ListaCongregaciones.Count == 0) return;

            List<SemanaCalendario> historialReal = new List<SemanaCalendario>();
            DateTime fechaBucle = new DateTime(2020, 1, 1);
            while (fechaBucle.DayOfWeek != DayOfWeek.Tuesday) fechaBucle = fechaBucle.AddDays(1);

            DateTime limite = DateTime.Today.AddDays(365);

            while (fechaBucle <= limite)
            {
                string isoId = fechaBucle.ToString("yyyy-MM-dd");
                var semanas = DatabaseHelper.ObtenerTodasSemanasPorFecha(isoId);
                if (semanas != null)
                {
                    foreach (var sem in semanas)
                    {
                        if (!string.IsNullOrEmpty(sem.Congregacion) && sem.Tipo != null && sem.Tipo.Contains("Visita") && ListaCongregaciones.Contains(sem.Congregacion.Trim()))
                            historialReal.Add(sem);
                    }
                }
                fechaBucle = fechaBucle.AddDays(7);
            }

            if (historialReal.Count > 0)
            {
                _cargando = true;
                int tamañoRueda = ListaCongregaciones.Count;

                for (int i = 0; i < historialReal.Count; i++)
                {
                    int numeroVisitaGlobal = i;
                    int ruedaActual = (numeroVisitaGlobal / tamañoRueda) + 1;
                    int posicionEnRueda = numeroVisitaGlobal % tamañoRueda;

                    if (ruedaActual > 6) break;

                    string nombreCongregacion = historialReal[i].Congregacion.Trim();
                    FilaCiclo fila = FilasCiclo[posicionEnRueda];

                    switch (ruedaActual)
                    {
                        case 1: if (!fila.L1) fila.Visita1 = nombreCongregacion; break;
                        case 2: if (!fila.L2) fila.Visita2 = nombreCongregacion; break;
                        case 3: if (!fila.L3) fila.Visita3 = nombreCongregacion; break;
                        case 4: if (!fila.L4) fila.Visita4 = nombreCongregacion; break;
                        case 5: if (!fila.L5) fila.Visita5 = nombreCongregacion; break;
                        case 6: if (!fila.L6) fila.Visita6 = nombreCongregacion; break;
                    }
                }
                _cargando = false;
            }
        }

        private void GenerarEstructuraBase()
        {
            _cargando = true;
            FilasCiclo.Clear();
            for (int i = 0; i < ListaCongregaciones.Count; i++)
            {
                var nueva = new FilaCiclo { Orden = i + 1 };
                nueva.PropertyChanged += Fila_PropertyChanged;
                FilasCiclo.Add(nueva);
            }
            _cargando = false;
        }

        private void BtnLimpiar_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("¿Seguro que deseas vaciar las celdas futuras no bloqueadas?", "Limpiar Matriz", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _cargando = true;
                foreach (var fila in FilasCiclo)
                {
                    if (!fila.L1) fila.Visita1 = null;
                    if (!fila.L2) fila.Visita2 = null;
                    if (!fila.L3) fila.Visita3 = null;
                    if (!fila.L4) fila.Visita4 = null;
                    if (!fila.L5) fila.Visita5 = null;
                    if (!fila.L6) fila.Visita6 = null;
                }
                SincronizarHistorialSilencioso();
                _cargando = false;
                GuardarSilenciosoAsync();
            }
        }

        // =========================================================================================
        // MOTOR DE RESOLUCIÓN (BACKTRACKING): Asigna posiciones resolviendo los choques automáticamente
        // =========================================================================================
        private bool Backtrack(
            int idx,
            List<string> porAsignar,
            Dictionary<string, int> prevPositions,
            Dictionary<string, int> direcciones,
            string[] asignaciones,
            int n,
            int tolerancia,
            ref int iteraciones)
        {
            iteraciones++;
            // Seguridad: Si la tolerancia actual es muy restrictiva y se atasca resolviendo choques, 
            // aborta rápido para pasar al siguiente nivel de tolerancia más flexible.
            if (iteraciones > 2000) return false;

            if (idx == porAsignar.Count) return true; // ¡Todas asignadas con éxito!

            string cong = porAsignar[idx];
            int prev = prevPositions.ContainsKey(cong) ? prevPositions[cong] : -1;
            int dir = direcciones.ContainsKey(cong) ? direcciones[cong] : 1;

            // 1. Calcular el salto ideal de 3 en 3 con rebote en las paredes
            int ideal = prev;
            if (prev != -1)
            {
                ideal = prev + (3 * dir);

                // Si rompe el suelo (rebota hacia arriba)
                if (ideal >= n)
                {
                    int rebote = ideal - (n - 1);
                    ideal = (n - 1) - rebote;
                }
                // Si rompe el techo (rebota hacia abajo)
                else if (ideal < 0)
                {
                    ideal = Math.Abs(ideal);
                }
            }
            else
            {
                ideal = n / 2; // Por defecto si no hay historia
            }

            // 2. Buscar qué huecos libres cumplen nuestras estrictas reglas de salto
            var slots = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (string.IsNullOrEmpty(asignaciones[i]))
                {
                    if (prev != -1)
                    {
                        int dist = Math.Abs(i - prev);
                        bool valid = false;

                        // Niveles de exigencia ante los choques:
                        if (tolerancia == 0) valid = (dist >= 2 && dist <= 4); // Ideal: Salta 2, 3 o 4 posiciones
                        else if (tolerancia == 1) valid = (dist >= 1 && dist <= 5); // Si se atasca: Permite 1 o 5
                        else if (tolerancia == 2) valid = (dist >= 1 && dist <= 6); // Peor caso: Permite hasta 6
                        else if (tolerancia == 3) valid = (dist >= 1); // ¡Prohibido quedarse quieto (0)!
                        else valid = true; // Supervivencia absoluta

                        if (valid) slots.Add(i);
                    }
                    else
                    {
                        slots.Add(i);
                    }
                }
            }

            // 3. Ordenar las opciones para probar siempre PRIMERO la más cercana al cálculo ideal (el 3)
            slots = slots.OrderBy(s => Math.Abs(s - ideal)).ToList();

            // 4. Probar las combinaciones como un puzzle
            foreach (int s in slots)
            {
                asignaciones[s] = cong;
                if (Backtrack(idx + 1, porAsignar, prevPositions, direcciones, asignaciones, n, tolerancia, ref iteraciones))
                    return true;

                asignaciones[s] = null; // Si falló más adelante, deshace y prueba el siguiente hueco
            }

            return false;
        }

        // =========================================================================================
        // BOTÓN AUTO-GENERAR
        // =========================================================================================
        private void BtnAutoGenerar_Click(object sender, RoutedEventArgs e)
        {
            if (ListaCongregaciones.Count == 0) return;

            if (MessageBox.Show("Se aplicará una rotación progresiva en 'Acordeón'. Las congregaciones subirán/bajarán rebotando en los extremos. En caso de choques, se buscará el hueco libre más cercano (saltos de 2 a 4). ¿Continuar?", "Auto-Configurar", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _cargando = true;
                int n = ListaCongregaciones.Count;

                // Limpiar celdas sin candado
                foreach (var fila in FilasCiclo)
                {
                    if (!fila.L1) fila.Visita1 = null;
                    if (!fila.L2) fila.Visita2 = null;
                    if (!fila.L3) fila.Visita3 = null;
                    if (!fila.L4) fila.Visita4 = null;
                    if (!fila.L5) fila.Visita5 = null;
                    if (!fila.L6) fila.Visita6 = null;
                }

                SincronizarHistorialSilencioso();

                // Llenar Origen (Visita 1)
                HashSet<string> usadasCol1 = new HashSet<string>();
                for (int r = 0; r < n; r++)
                {
                    string val = FilasCiclo[r].Visita1;
                    if (!string.IsNullOrEmpty(val)) usadasCol1.Add(val);
                }
                Queue<string> faltantesCol1 = new Queue<string>(ListaCongregaciones.Where(c => !usadasCol1.Contains(c)));
                for (int r = 0; r < n; r++)
                {
                    if (string.IsNullOrEmpty(FilasCiclo[r].Visita1) && faltantesCol1.Count > 0)
                        FilasCiclo[r].Visita1 = faltantesCol1.Dequeue();
                }

                // Definir direcciones iniciales (Mitad de arriba baja, mitad de abajo sube)
                Dictionary<string, int> direcciones = new Dictionary<string, int>();
                for (int r = 0; r < n; r++)
                {
                    string cong = FilasCiclo[r].Visita1;
                    if (!string.IsNullOrEmpty(cong)) direcciones[cong] = (r < n / 2) ? 1 : -1;
                }

                // Configurar de la Visita 2 a la 6
                for (int col = 2; col <= 6; col++)
                {
                    var prevPositions = new Dictionary<string, int>();
                    var usadasEnCol = new HashSet<string>();
                    string[] asignaciones = new string[n];

                    for (int r = 0; r < n; r++)
                    {
                        string valAnterior = GetValorColumna(FilasCiclo[r], col - 1);
                        if (!string.IsNullOrEmpty(valAnterior)) prevPositions[valAnterior] = r;

                        string valActual = GetValorColumna(FilasCiclo[r], col);
                        if (!string.IsNullOrEmpty(valActual))
                        {
                            asignaciones[r] = valActual; // Respetar el historial / bloqueos
                            usadasEnCol.Add(valActual);
                        }
                    }

                    var porAsignar = ListaCongregaciones.Where(c => !usadasEnCol.Contains(c)).ToList();

                    // TRUCO DE IA: Ordenar para procesar primero a las congregaciones que están en los bordes 
                    // (ya que tienen menos opciones de escape y son las primeras en bloquear el puzzle)
                    porAsignar = porAsignar.OrderBy(c => {
                        if (!prevPositions.ContainsKey(c)) return 0;
                        int p = prevPositions[c];
                        return Math.Min(p, n - 1 - p);
                    }).ToList();

                    // Resolver la columna subiendo la flexibilidad si hace falta
                    for (int tol = 0; tol <= 4; tol++)
                    {
                        int iteraciones = 0;
                        if (Backtrack(0, porAsignar, prevPositions, direcciones, asignaciones, n, tol, ref iteraciones))
                        {
                            break; // ¡Puzzle resuelto!
                        }
                    }

                    // Escribir los resultados en la tabla y actualizar las direcciones para la siguiente visita
                    for (int r = 0; r < n; r++)
                    {
                        string cong = asignaciones[r];
                        if (cong != null)
                        {
                            SetValorColumna(FilasCiclo[r], col, cong);

                            if (prevPositions.ContainsKey(cong))
                            {
                                int prev = prevPositions[cong];
                                if (r > prev) direcciones[cong] = 1; // Si acabó más abajo, actualizamos su inercia hacia abajo
                                else if (r < prev) direcciones[cong] = -1; // Si acabó más arriba, inercia hacia arriba
                            }
                        }
                    }
                }

                _cargando = false;
                GuardarSilenciosoAsync();
            }
        }

        private string GetValorColumna(FilaCiclo fila, int col)
        {
            switch (col)
            {
                case 1: return fila.Visita1;
                case 2: return fila.Visita2;
                case 3: return fila.Visita3;
                case 4: return fila.Visita4;
                case 5: return fila.Visita5;
                case 6: return fila.Visita6;
                default: return null;
            }
        }

        private void SetValorColumna(FilaCiclo fila, int col, string valor)
        {
            switch (col)
            {
                case 1: fila.Visita1 = valor; break;
                case 2: fila.Visita2 = valor; break;
                case 3: fila.Visita3 = valor; break;
                case 4: fila.Visita4 = valor; break;
                case 5: fila.Visita5 = valor; break;
                case 6: fila.Visita6 = valor; break;
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}