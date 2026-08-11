using System;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class FormularioPersonaWindow : Window
    {
        public Persona PersonaActualizada { get; private set; }

        public FormularioPersonaWindow(Persona persona)
        {
            InitializeComponent();

            // Clonamos el objeto por seguridad
            PersonaActualizada = new Persona
            {
                Id = persona.Id,
                Nombre = persona.Nombre,
                FechaNacimiento = persona.FechaNacimiento,
                Sexo = persona.Sexo,
                FechaBautismo = persona.FechaBautismo,
                Esperanza = persona.Esperanza,
                Nombramiento = persona.Nombramiento,
                Servicio = persona.Servicio,
                CongregacionCodigo = persona.CongregacionCodigo
            };

            // 1. Volcamos el nombre libremente
            TxtNombre.Text = PersonaActualizada.Nombre;

            // 2. Traducimos el texto de las fechas a fechas reales para los calendarios
            DpNacimiento.SelectedDate = ParsearFecha(PersonaActualizada.FechaNacimiento);
            DpBautismo.SelectedDate = ParsearFecha(PersonaActualizada.FechaBautismo);

            // 3. Seleccionamos el ítem correcto en los menús desplegables
            AsignarValorComboBox(CmbSexo, PersonaActualizada.Sexo);
            AsignarValorComboBox(CmbEsperanza, PersonaActualizada.Esperanza);
            AsignarValorComboBox(CmbNombramiento, PersonaActualizada.Nombramiento);
            AsignarValorComboBox(CmbServicio, PersonaActualizada.Servicio);
        }

        // ==========================================
        // MÉTODOS AUXILIARES DE CONVERSIÓN
        // ==========================================

        private DateTime? ParsearFecha(string textoFecha)
        {
            if (string.IsNullOrWhiteSpace(textoFecha)) return null;

            // Intentamos leer el formato exacto de tu JSON "dd-MM-yyyy" (ej. "26-02-1976")
            if (DateTime.TryParseExact(textoFecha, "dd-MM-yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime fechaFiltro))
                return fechaFiltro;

            // Si tiene otro formato, dejamos que .NET intente adivinarlo
            if (DateTime.TryParse(textoFecha, out DateTime fechaNormal))
                return fechaNormal;

            return null; // Si es un texto inválido o vacío, dejamos el calendario en blanco
        }

        private void AsignarValorComboBox(ComboBox comboBox, string valorObjetivo)
        {
            if (string.IsNullOrEmpty(valorObjetivo)) valorObjetivo = ""; // Tratamos nulos como vacíos

            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Content.ToString() == valorObjetivo)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            // Blindaje: Si el valor de la BD no coincide con las opciones (ej. error de un JSON viejo), 
            // creamos la opción al vuelo para no perder el dato original.
            var nuevoItem = new ComboBoxItem { Content = valorObjetivo };
            comboBox.Items.Add(nuevoItem);
            comboBox.SelectedItem = nuevoItem;
        }

        // ==========================================
        // GUARDAR O CANCELAR
        // ==========================================

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNombre.Text))
            {
                MessageBox.Show("El nombre no puede estar vacío.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PersonaActualizada.Nombre = TxtNombre.Text.Trim();

            // Devolvemos las fechas del calendario al formato texto original esperado por la base de datos
            PersonaActualizada.FechaNacimiento = DpNacimiento.SelectedDate.HasValue
                ? DpNacimiento.SelectedDate.Value.ToString("dd-MM-yyyy")
                : "";

            PersonaActualizada.FechaBautismo = DpBautismo.SelectedDate.HasValue
                ? DpBautismo.SelectedDate.Value.ToString("dd-MM-yyyy")
                : "";

            // Extraemos los textos seleccionados de los desplegables
            PersonaActualizada.Sexo = (CmbSexo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            PersonaActualizada.Esperanza = (CmbEsperanza.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            PersonaActualizada.Nombramiento = (CmbNombramiento.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            PersonaActualizada.Servicio = (CmbServicio.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";

            this.DialogResult = true;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}