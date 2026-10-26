using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace SCCosmos.Mobile.Platforms.Android;

// =============================================================
// SERVICIO DEL WIDGET (LISTA DESPLAZABLE)
//
// El widget es un «widget de colección»: la cabecera (icono, título y
// flechas de día) es fija y el contenido (programa, tareas, alertas y
// predicación) va en una lista que se puede deslizar si no cabe.
// =============================================================

[Service(
    Exported = false,
    Permission = "android.permission.BIND_REMOTEVIEWS")]
public class SccosmosWidgetService : RemoteViewsService
{
    public override IRemoteViewsFactory OnGetViewFactory(
        Intent? intent)
    {
        Context contexto =
            ApplicationContext
            ?? global::Android.App.Application.Context;

        return new SccosmosWidgetFactory(contexto);
    }
}


public class SccosmosWidgetFactory
    : Java.Lang.Object, RemoteViewsService.IRemoteViewsFactory
{
    private const int TipoSeccion =
        0;

    private const int TipoPrograma =
        1;

    private const int TipoTarea =
        2;

    private const int TipoAlerta =
        3;

    private const int TipoPredicacion =
        4;


    private sealed class Fila
    {
        public int Tipo;
        public string Texto = "";
        public string Secundaria = "";
        public string Tiempo = "";
        public string ConsultaMapa = "";
        public string Color = "";
        public string Firma = "";
        public int TareaId;
        public bool Vencida;
    }


    private readonly Context _contexto;

    private readonly List<Fila> _filas =
        new();


    public SccosmosWidgetFactory(
        Context contexto)
    {
        _contexto = contexto;
    }


    public int Count =>
        _filas.Count;

    public bool HasStableIds =>
        false;

    public RemoteViews? LoadingView =>
        null;

    public RemoteViews? EmptyView =>
        null;

    public int ViewTypeCount =>
        5;


    public void OnCreate()
    {
    }


    public void OnDestroy()
    {
    }


    public void OnDataSetChanged()
    {
        CargarFilas();
    }


    public long GetItemId(
        int position) =>
        position;


    public int GetItemViewType(
        int position) =>
        _filas[position].Tipo;


    private void CargarFilas()
    {
        _filas.Clear();

        try
        {
            DateTime dia =
                SccosmosWidgetProvider
                    .ObtenerDiaSeleccionado();

            // ---- PROGRAMA DE ACTIVIDAD ----

            _filas.Add(new Fila
            {
                Tipo = TipoSeccion,
                Texto = "PROGRAMA DE ACTIVIDAD",
                Color = "#4BA3E3"
            });

            List<(string Texto, string ConsultaMapa, string Descripcion, string Tiempo)>
                programa =
                    SccosmosWidgetProvider
                        .ObtenerProgramaDelDia(dia);

            if (programa.Count == 0)
            {
                _filas.Add(new Fila
                {
                    Tipo = TipoPrograma,
                    Texto = dia.Date == DateTime.Today
                        ? "Sin actividades pendientes por ahora"
                        : "Sin programación para ese día"
                });
            }
            else
            {
                foreach (var evento in programa)
                {
                    _filas.Add(new Fila
                    {
                        Tipo = TipoPrograma,
                        Texto = evento.Texto,
                        Secundaria = evento.Descripcion,
                        Tiempo = evento.Tiempo,
                        ConsultaMapa = evento.ConsultaMapa
                    });
                }
            }

            // ---- TAREAS ----

            var tareasInfo =
                SccosmosWidgetProvider
                    .ObtenerTareasDelDia(dia);

            string tituloTareas =
                tareasInfo.Vencidas > 0
                    ? "TAREAS · " + tareasInfo.Vencidas
                      + (tareasInfo.Vencidas == 1
                          ? " ATRASADA"
                          : " ATRASADAS")
                    : "TAREAS";

            _filas.Add(new Fila
            {
                Tipo = TipoSeccion,
                Texto = tituloTareas,
                Color = tareasInfo.Vencidas > 0
                    ? SccosmosWidgetProvider.ColorTareaVencida
                    : SccosmosWidgetProvider.ColorSeccionTareas
            });

            if (tareasInfo.Tareas.Count == 0)
            {
                _filas.Add(new Fila
                {
                    Tipo = TipoTarea,
                    Texto = "Sin tareas para ese día"
                });
            }
            else
            {
                foreach (var tarea in tareasInfo.Tareas)
                {
                    _filas.Add(new Fila
                    {
                        Tipo = TipoTarea,
                        Texto = tarea.Texto,
                        TareaId = tarea.Id,
                        Vencida = tarea.Vencida
                    });
                }
            }

            // ---- ALERTAS (solo si hay) ----

            List<(string Firma, string Texto)> alertas =
                SccosmosWidgetProvider
                    .ObtenerAlertasPendientes();

            if (alertas.Count > 0)
            {
                _filas.Add(new Fila
                {
                    Tipo = TipoSeccion,
                    Texto = "ALERTAS PENDIENTES",
                    Color = "#FFB74D"
                });

                foreach (var alerta in alertas)
                {
                    _filas.Add(new Fila
                    {
                        Tipo = TipoAlerta,
                        Texto = alerta.Texto,
                        Firma = alerta.Firma
                    });
                }
            }

            // ---- PREDICACIÓN (mes y año en vigor) ----

            (string Titulo, string Texto) predicacion =
                SccosmosWidgetProvider
                    .ObtenerPredicacionWidget();

            _filas.Add(new Fila
            {
                Tipo = TipoSeccion,
                Texto = predicacion.Titulo,
                Color = "#9CC3F5"
            });

            _filas.Add(new Fila
            {
                Tipo = TipoPredicacion,
                Texto = predicacion.Texto
            });
        }
        catch
        {
        }
    }


    public RemoteViews? GetViewAt(
        int position)
    {
        try
        {
            if (position < 0
                || position >= _filas.Count)
            {
                return null;
            }

            Fila fila =
                _filas[position];

            switch (fila.Tipo)
            {
                case TipoSeccion:
                {
                    var vistas =
                        new RemoteViews(
                            _contexto.PackageName,
                            Resource.Layout.widget_fila_seccion);

                    vistas.SetTextViewText(
                        Resource.Id.fila_seccion_texto,
                        fila.Texto);

                    if (!string.IsNullOrWhiteSpace(fila.Color))
                    {
                        vistas.SetTextColor(
                            Resource.Id.fila_seccion_texto,
                            global::Android.Graphics.Color.ParseColor(fila.Color));
                    }

                    return vistas;
                }

                case TipoPrograma:
                {
                    var vistas =
                        new RemoteViews(
                            _contexto.PackageName,
                            Resource.Layout.widget_fila_programa);

                    vistas.SetTextViewText(
                        Resource.Id.fila_programa_texto,
                        fila.Texto);

                    // Línea secundaria: descripción y tiempo en coche.
                    string secundaria =
                        fila.Secundaria;

                    if (!string.IsNullOrWhiteSpace(
                            fila.Tiempo))
                    {
                        secundaria =
                            string.IsNullOrWhiteSpace(secundaria)
                                ? fila.Tiempo
                                : secundaria + " · " + fila.Tiempo;
                    }

                    if (!string.IsNullOrWhiteSpace(
                            secundaria))
                    {
                        vistas.SetTextViewText(
                            Resource.Id.fila_programa_secundaria,
                            secundaria);

                        vistas.SetViewVisibility(
                            Resource.Id.fila_programa_secundaria,
                            ViewStates.Visible);
                    }

                    if (!string.IsNullOrWhiteSpace(
                            fila.ConsultaMapa))
                    {
                        SccosmosWidgetProvider.PonerClicItem(
                            vistas,
                            Resource.Id.fila_programa_mapa,
                            "mapa",
                            consulta: fila.ConsultaMapa);

                        vistas.SetViewVisibility(
                            Resource.Id.fila_programa_mapa,
                            ViewStates.Visible);
                    }

                    SccosmosWidgetProvider.PonerClicItem(
                        vistas,
                        Resource.Id.fila_programa_raiz,
                        "abrir",
                        destino: "visita");

                    return vistas;
                }

                case TipoTarea:
                {
                    var vistas =
                        new RemoteViews(
                            _contexto.PackageName,
                            Resource.Layout.widget_fila_tarea);

                    vistas.SetTextViewText(
                        Resource.Id.fila_tarea_texto,
                        fila.Texto);

                    vistas.SetTextColor(
                        Resource.Id.fila_tarea_texto,
                        fila.Vencida
                            ? global::Android.Graphics.Color.ParseColor(
                                SccosmosWidgetProvider.ColorTareaVencida)
                            : global::Android.Graphics.Color.ParseColor("#E6E8EB"));

                    if (fila.TareaId > 0)
                    {
                        SccosmosWidgetProvider.PonerClicItem(
                            vistas,
                            Resource.Id.fila_tarea_ok,
                            "tarea",
                            tarea: fila.TareaId);

                        vistas.SetViewVisibility(
                            Resource.Id.fila_tarea_ok,
                            ViewStates.Visible);
                    }

                    SccosmosWidgetProvider.PonerClicItem(
                        vistas,
                        Resource.Id.fila_tarea_raiz,
                        "abrir",
                        destino: "tareas");

                    return vistas;
                }

                case TipoAlerta:
                {
                    var vistas =
                        new RemoteViews(
                            _contexto.PackageName,
                            Resource.Layout.widget_fila_alerta);

                    vistas.SetTextViewText(
                        Resource.Id.fila_alerta_texto,
                        fila.Texto);

                    if (!string.IsNullOrWhiteSpace(
                            fila.Firma))
                    {
                        SccosmosWidgetProvider.PonerClicItem(
                            vistas,
                            Resource.Id.fila_alerta_ok,
                            "alerta",
                            alerta: fila.Firma);

                        vistas.SetViewVisibility(
                            Resource.Id.fila_alerta_ok,
                            ViewStates.Visible);
                    }

                    SccosmosWidgetProvider.PonerClicItem(
                        vistas,
                        Resource.Id.fila_alerta_raiz,
                        "abrir",
                        destino: "alertas");

                    return vistas;
                }

                case TipoPredicacion:
                {
                    var vistas =
                        new RemoteViews(
                            _contexto.PackageName,
                            Resource.Layout.widget_fila_predicacion);

                    vistas.SetTextViewText(
                        Resource.Id.fila_predicacion_texto,
                        fila.Texto);

                    SccosmosWidgetProvider.PonerClicItem(
                        vistas,
                        Resource.Id.fila_predicacion_mas,
                        "horas");

                    vistas.SetViewVisibility(
                        Resource.Id.fila_predicacion_mas,
                        ViewStates.Visible);

                    return vistas;
                }
            }
        }
        catch
        {
        }

        return null;
    }
}
