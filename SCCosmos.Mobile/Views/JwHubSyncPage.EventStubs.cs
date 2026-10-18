using System;

namespace SCCosmos.Mobile.Views;

#if !ANDROID

// =============================================================
// STUBS PARA LOS TARGETS NO ANDROID
// =============================================================
//
// SCCosmos.Mobile está configurado como proyecto MAUI multitarget.
//
// La implementación real de estas acciones existe únicamente
// dentro de bloques #if ANDROID en sus respectivos code-behind.
//
// Sin estos stubs, el compilador XAML de MAUI intenta resolver
// los handlers también al compilar Windows/iOS/MacCatalyst y
// muestra errores como:
//
// "Event handler '...' with correct signature not found".
//
// En Android estos métodos NO se compilan, por lo que no
// interfieren en absoluto con la lógica real de la aplicación.
// =============================================================

public partial class JwHubSyncConflictPage
{
    private void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        // No se usa fuera de Android.
    }
}


public partial class JwHubSyncNewPersonPage
{
    private void BtnVincular_Clicked(
        object sender,
        EventArgs e)
    {
        // No se usa fuera de Android.
    }


    private void BtnAnadir_Clicked(
        object sender,
        EventArgs e)
    {
        // No se usa fuera de Android.
    }
}

#endif
