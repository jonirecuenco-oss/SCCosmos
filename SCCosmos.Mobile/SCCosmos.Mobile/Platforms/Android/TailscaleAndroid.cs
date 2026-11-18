using Android.Content;

using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Si la app de Tailscale está instalada y no hay ninguna VPN activa, le pide
/// que se conecte (con ella se sincroniza fuera de casa). Usa la orden que
/// Tailscale ofrece a otras apps (com.tailscale.ipn.CONNECT_VPN). Si hay otra
/// VPN activa, no se toca. Lo más fiable sigue siendo activar en Android
/// «VPN siempre activa» para Tailscale.
/// </summary>
public static class TailscaleAndroid
{
    private const string Paquete =
        "com.tailscale.ipn";

    private const string Receptor =
        "com.tailscale.ipn.IPNReceiver";

    private const string OrdenConectar =
        "com.tailscale.ipn.CONNECT_VPN";


    public static void Asegurar()
    {
        try
        {
            Context contexto =
                global::Android.App.Application.Context;

            if (!Instalado(contexto)
                || VpnActiva(contexto))
            {
                return;
            }

            var orden =
                new Intent(OrdenConectar);

            orden.SetComponent(
                new ComponentName(
                    Paquete,
                    Receptor));

            contexto.SendBroadcast(
                orden);

            Perf.Registrar(
                "Tailscale.Conectar",
                0);
        }
        catch (Exception ex)
        {
            RegistroErrores.Anotar(
                ex,
                "Tailscale.Conectar");
        }
    }


    // Visible gracias a <queries> en el manifiesto (desde Android 11).
    private static bool Instalado(
        Context contexto) =>
        contexto.PackageManager?.GetLaunchIntentForPackage(
            Paquete) is not null;


    private static bool VpnActiva(
        Context contexto)
    {
        if (contexto.GetSystemService(
                Context.ConnectivityService)
            is not global::Android.Net.ConnectivityManager conectividad)
        {
            // Sin poder saberlo, no se toca.
            return true;
        }

#pragma warning disable CS0618, CA1422 // GetAllNetworks: obsoleta desde Android 12, pero sigue funcionando.
        global::Android.Net.Network[] redes =
            conectividad.GetAllNetworks();
#pragma warning restore CS0618, CA1422

        foreach (global::Android.Net.Network red in redes)
        {
            if (conectividad
                    .GetNetworkCapabilities(red)
                    ?.HasTransport(global::Android.Net.TransportType.Vpn)
                == true)
            {
                return true;
            }
        }

        return false;
    }
}
