using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SCCosmos
{
    /// <summary>
    /// Las páginas que saben refrescarse solas (sin recrearse)
    /// implementan esta interfaz.
    /// </summary>
    public interface IActualizablePorSincro
    {
        void DatosActualizados();
    }


    /// <summary>
    /// Cuando llegan cambios de otro dispositivo, refresca la página
    /// abierta automáticamente:
    ///
    /// - Si la página sabe refrescarse (IActualizablePorSincro), se
    ///   le pide que recargue sus datos.
    /// - Si es una página sencilla del menú, se vuelve a abrir.
    /// - En cualquier caso solo actúa sobre la página visible.
    /// </summary>
    public static class SincronizacionPaginas
    {
        private static readonly TimeSpan Espera =
            TimeSpan.FromSeconds(2);

        private static MainWindow? _ventana;

        private static Page? _paginaActual;

        private static bool _pendiente;

        private static DispatcherTimer? _temporizador;


        public static void Iniciar(
            MainWindow ventana)
        {
            _ventana =
                ventana;

            SincronizacionRed.DatosRecibidos +=
                _ => Programar();

            ventana.MenuPrincipal.Navigated +=
                (s, e) =>
                {
                    try
                    {
                        _paginaActual =
                            e.Page as Page;

                        // Si llegó algo mientras estabas en otra
                        // pantalla, se aplica al volver.
                        if (_pendiente)
                        {
                            Disparar();
                        }
                    }
                    catch
                    {
                    }
                };
        }


        private static void Programar()
        {
            try
            {
                Application.Current?.Dispatcher
                    .Invoke(
                        () =>
                        {
                            _pendiente =
                                true;

                            _temporizador ??=
                                new DispatcherTimer
                                {
                                    Interval =
                                        Espera
                                };

                            _temporizador.Stop();

                            _temporizador.Tick -=
                                Temporizador_Tick;

                            _temporizador.Tick +=
                                Temporizador_Tick;

                            _temporizador.Start();
                        });
            }
            catch
            {
            }
        }


        private static void Temporizador_Tick(
            object? sender,
            EventArgs e)
        {
            Disparar();
        }


        private static void Disparar()
        {
            try
            {
                _temporizador?.Stop();

                if (!_pendiente)
                {
                    return;
                }

                _pendiente =
                    false;

                Page? pagina =
                    _paginaActual;

                if (pagina is null)
                {
                    return;
                }

                if (pagina is IActualizablePorSincro actualizable)
                {
                    actualizable.DatosActualizados();

                    return;
                }

                // Páginas sencillas del menú: volver a abrirlas es
                // equivalente a lo que hacía el usuario a mano.
                if (EsPaginaSencilla(
                        pagina))
                {
                    _ventana?
                        .MenuPrincipal
                        .Navigate(
                            pagina.GetType());
                }
            }
            catch
            {
            }
        }


        private static bool EsPaginaSencilla(
            Page pagina)
        {
            return pagina is AsambleasPage
                   || pagina is VisitasPage
                   || pagina is DocumentacionPage;
        }
    }
}
