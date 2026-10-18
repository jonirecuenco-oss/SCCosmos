using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Services;

public static class PersonasNavigationState
{
    private static FiltrosPersonas? _filtrosPendientes;

    public static void Establecer(FiltrosPersonas filtros)
    {
        _filtrosPendientes = filtros.Clonar();
    }

    public static FiltrosPersonas? Consumir()
    {
        var filtros = _filtrosPendientes;
        _filtrosPendientes = null;

        return filtros;
    }
}