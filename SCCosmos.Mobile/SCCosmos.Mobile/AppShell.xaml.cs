using SCCosmos.Mobile.Services;
using SCCosmos.Mobile.Views;

namespace SCCosmos.Mobile
{
    public partial class AppShell : Shell
    {
        private bool _perfilSolicitado;

        private Page? _paginaAdaptada;

        public AppShell()
        {
            InitializeComponent();

            AplicarPerfilAcceso();
        }

        protected override void OnNavigated(
            ShellNavigatedEventArgs args)
        {
            base.OnNavigated(
                args);

            AplicarPerfilAcceso();
            ConectarAdaptacion();
        }

        /// <summary>
        /// Adapta la página que se está viendo al tamaño de la ventana y la
        /// vuelve a adaptar cuando cambia (girar el móvil, desplegar el
        /// plegable, cambiar de ventana en una tableta).
        /// </summary>
        private void ConectarAdaptacion()
        {
            Page? pagina =
                CurrentPage;

            if (pagina is null
                ||
                ReferenceEquals(
                    pagina,
                    _paginaAdaptada))
            {
                return;
            }

            if (_paginaAdaptada is not null)
            {
                _paginaAdaptada.SizeChanged -=
                    Pagina_SizeChanged;
            }

            _paginaAdaptada =
                pagina;

            pagina.SizeChanged +=
                Pagina_SizeChanged;

            AdaptarPagina(
                pagina);
        }

        private void Pagina_SizeChanged(
            object? sender,
            EventArgs e)
        {
            if (sender is Page pagina)
            {
                AdaptarPagina(
                    pagina);
            }
        }

        private void AdaptarPagina(
            Page pagina)
        {
            if (pagina is ContentPage contentPage)
            {
                AdaptadorPantalla.Aplicar(
                    contentPage,
                    pagina.Width);
            }

            // En pantallas grandes el menú lateral se queda fijo.
            FlyoutBehavior =
                pagina.Width >= 1000
                    ? FlyoutBehavior.Locked
                    : FlyoutBehavior.Flyout;
        }

        public void AplicarPerfilAcceso()
        {
            bool completo =
                PerfilAccesoService.AccesoCompleto;

            ItemCorrespondencia.IsVisible = completo;
            ItemCircuito.IsVisible = completo;
            ItemArchivo.IsVisible = completo;
            ItemAsambleas.IsVisible = completo;
            ItemEscuelas.IsVisible = completo;
            ItemActividad.IsVisible = completo;

            // La RSC la ve también la esposa (el SC la va rellenando en
            // directo y ella lo sigue desde su dispositivo).
            ItemRsc.IsVisible = true;

            // Donaciones la ven tanto el SC como su esposa.
            ItemDonaciones.IsVisible = true;
        }


        /// <summary>
        /// El pie del menú lleva a Configuración (oculta de la lista).
        /// </summary>
        private void Configuracion_Tapped(
            object sender,
            TappedEventArgs e)
        {
            try
            {
                FlyoutIsPresented =
                    false;

                _ = GoToAsync(
                    "//configuracion");
            }
            catch
            {
            }
        }

        /// <summary>
        /// Muestra la selección de perfil la primera vez (si aún no se
        /// ha configurado).
        /// </summary>
        public async Task SolicitarPerfilSiProcedeAsync()
        {
            if (_perfilSolicitado
                ||
                PerfilAccesoService.Configurado)
            {
                return;
            }

            _perfilSolicitado =
                true;

            await Navigation.PushModalAsync(
                new NavigationPage(
                    new PerfilInicialPage()));
        }
    }
}
