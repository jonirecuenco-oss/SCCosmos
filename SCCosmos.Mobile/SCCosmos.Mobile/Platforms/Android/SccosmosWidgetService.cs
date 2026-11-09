using SCCosmos.Sincro;
using System.Globalization;
using SCCosmos.Mobile.Services;
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

    private const int TipoTiempo =
        5;

    private const int TipoAvisoTiempo =
        6;


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
        public List<HoraTiempo>? Horas;
        public bool TiempoAntes;
        public bool TiempoDespues;
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
        7;


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

            // ---- TIEMPO (AEMET) del día elegido, donde se va a estar ----

            AgregarTiempo(dia);

            // ---- PREDICACIÓN (mes y año en vigor): abajo del todo ----

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


    /// <summary>
    /// Tiempo de AEMET al final: lo que haya en la caché (lo refresca Inicio y
    /// la revisión de cada 2 horas). Avisos y, debajo, 6 horas (o los tramos).
    /// </summary>
    private void AgregarTiempo(DateTime dia)
    {
        try
        {
            TiempoDia? tiempo =
                TiempoAemetService.ObtenerDeCache(dia);

            if (tiempo is null
                || tiempo.SinPrevision
                || tiempo.Horas.Count == 0)
            {
                return;
            }

            string extremos =
                (tiempo.Maxima is int maxima && tiempo.Minima is int minima
                    ? $" · {maxima}° / {minima}°"
                    : "")
                + (tiempo.ProbLluviaDia is int lluvia && lluvia > 0
                    ? $" · LLUVIA {lluvia}%"
                    : "");

            _filas.Add(new Fila
            {
                Tipo = TipoSeccion,
                Texto = "TIEMPO · " + tiempo.Lugar.ToUpper(CultureInfo.GetCultureInfo("es-ES")) + extremos,
                Color = "#F5C451"
            });

            List<HoraTiempo> horas =
                HorasParaWidget(tiempo, out bool antes, out bool despues);


            _filas.Add(new Fila
            {
                Tipo = TipoTiempo,
                Horas = horas,
                TiempoAntes = antes,
                TiempoDespues = despues
            });
        }
        catch
        {
        }
    }


    /// <summary>
    /// 6 horas seguidas: hoy desde la hora actual y otro día desde las 8. Con
    /// las flechas se mueve de 6 en 6 (lo guarda el proveedor por día).
    /// </summary>
    private static List<HoraTiempo> HorasParaWidget(
        TiempoDia tiempo,
        out bool antes,
        out bool despues)
    {
        antes = false;
        despues = false;

        List<HoraTiempo> horas = tiempo.Horas;

        if (!tiempo.PorHoras || horas.Count <= 6)
        {
            return horas.Take(6).ToList();
        }

        int inicio =
            Math.Max(0, horas.FindIndex(h => h.Hora >= (tiempo.Fecha == DateTime.Today ? DateTime.Now.Hour : 8)))
            + SccosmosWidgetProvider.DesplazamientoTiempo(tiempo.Fecha);

        inicio = Math.Clamp(inicio, 0, horas.Count - 6);

        antes = inicio > 0;
        despues = inicio + 6 < horas.Count;

        return horas.Skip(inicio).Take(6).ToList();
    }


    private static int IconoWidget(string icono) =>
        icono switch
        {
            "tiempo_sol" => Resource.Drawable.widget_tiempo_sol,
            "tiempo_luna" => Resource.Drawable.widget_tiempo_luna,
            "tiempo_sol_nubes" => Resource.Drawable.widget_tiempo_sol_nubes,
            "tiempo_luna_nubes" => Resource.Drawable.widget_tiempo_luna_nubes,
            "tiempo_lluvia" => Resource.Drawable.widget_tiempo_lluvia,
            "tiempo_llovizna" => Resource.Drawable.widget_tiempo_llovizna,
            "tiempo_tormenta" => Resource.Drawable.widget_tiempo_tormenta,
            "tiempo_nieve" => Resource.Drawable.widget_tiempo_nieve,
            "tiempo_niebla" => Resource.Drawable.widget_tiempo_niebla,
            _ => Resource.Drawable.widget_tiempo_nubes
        };

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

                    // Se limpian los restos de vistas reutilizadas por la
                    // lista (antes quedaban el 📍 y la descripción del día
                    // anterior).
                    vistas.SetViewVisibility(
                        Resource.Id.fila_programa_mapa,
                        ViewStates.Gone);

                    vistas.SetTextViewText(
                        Resource.Id.fila_programa_secundaria,
                        "");

                    vistas.SetViewVisibility(
                        Resource.Id.fila_programa_secundaria,
                        ViewStates.Gone);

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

                    vistas.SetViewVisibility(
                        Resource.Id.fila_tarea_ok,
                        ViewStates.Gone);

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

                    vistas.SetViewVisibility(
                        Resource.Id.fila_alerta_ok,
                        ViewStates.Gone);

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

                case TipoTiempo:
                {
                    var vistas =
                        new RemoteViews(
                            _contexto.PackageName,
                            Resource.Layout.widget_fila_tiempo);

                    int[] celdas = { Resource.Id.tiempo_celda_1, Resource.Id.tiempo_celda_2, Resource.Id.tiempo_celda_3, Resource.Id.tiempo_celda_4, Resource.Id.tiempo_celda_5, Resource.Id.tiempo_celda_6 };
                    int[] horas = { Resource.Id.tiempo_hora_1, Resource.Id.tiempo_hora_2, Resource.Id.tiempo_hora_3, Resource.Id.tiempo_hora_4, Resource.Id.tiempo_hora_5, Resource.Id.tiempo_hora_6 };
                    int[] iconos = { Resource.Id.tiempo_icono_1, Resource.Id.tiempo_icono_2, Resource.Id.tiempo_icono_3, Resource.Id.tiempo_icono_4, Resource.Id.tiempo_icono_5, Resource.Id.tiempo_icono_6 };
                    int[] temperaturas = { Resource.Id.tiempo_temp_1, Resource.Id.tiempo_temp_2, Resource.Id.tiempo_temp_3, Resource.Id.tiempo_temp_4, Resource.Id.tiempo_temp_5, Resource.Id.tiempo_temp_6 };
                    int[] lluvias = { Resource.Id.tiempo_lluvia_1, Resource.Id.tiempo_lluvia_2, Resource.Id.tiempo_lluvia_3, Resource.Id.tiempo_lluvia_4, Resource.Id.tiempo_lluvia_5, Resource.Id.tiempo_lluvia_6 };

                    int[] avisos = { Resource.Id.tiempo_aviso_1, Resource.Id.tiempo_aviso_2, Resource.Id.tiempo_aviso_3, Resource.Id.tiempo_aviso_4, Resource.Id.tiempo_aviso_5, Resource.Id.tiempo_aviso_6 };

                    List<HoraTiempo> lista =
                        fila.Horas ?? new List<HoraTiempo>();

                    for (int i = 0; i < celdas.Length; i++)
                    {
                        if (i >= lista.Count)
                        {
                            vistas.SetViewVisibility(celdas[i], ViewStates.Gone);
                            continue;
                        }

                        HoraTiempo hora = lista[i];

                        vistas.SetTextViewText(horas[i], hora.Etiqueta);
                        vistas.SetImageViewResource(iconos[i], IconoWidget(hora.Icono));
                        vistas.SetTextViewText(temperaturas[i], hora.Temperatura is int t ? $"{t}°" : "");
                        vistas.SetTextViewText(lluvias[i], hora.TextoLluvia);

                        // El aviso que cubre esa hora: su icono, del color del nivel.
                        if (hora.Aviso is AvisoTiempo aviso)
                        {
                            vistas.SetViewVisibility(avisos[i], ViewStates.Visible);
                            vistas.SetImageViewResource(
                                avisos[i],
                                aviso.NivelIcono switch
                                {
                                    "rojo" => Resource.Drawable.widget_aviso_rojo,
                                    "naranja" => Resource.Drawable.widget_aviso_naranja,
                                    _ => Resource.Drawable.widget_aviso_amarillo
                                });
                            vistas.SetContentDescription(avisos[i], aviso.Titulo);
                        }
                        else
                        {
                            vistas.SetViewVisibility(avisos[i], ViewStates.Gone);
                        }
                    }

                    // Flechas para moverse por las horas (si hay más antes o después).
                    vistas.SetViewVisibility(Resource.Id.tiempo_antes, fila.TiempoAntes ? ViewStates.Visible : ViewStates.Invisible);
                    vistas.SetViewVisibility(Resource.Id.tiempo_despues, fila.TiempoDespues ? ViewStates.Visible : ViewStates.Invisible);

                    SccosmosWidgetProvider.PonerClicItem(vistas, Resource.Id.tiempo_antes, "tiempo_antes");
                    SccosmosWidgetProvider.PonerClicItem(vistas, Resource.Id.tiempo_despues, "tiempo_despues");

                    SccosmosWidgetProvider.PonerClicItem(
                        vistas,
                        Resource.Id.fila_tiempo_raiz,
                        "abrir");

                    return vistas;
                }

                case TipoAvisoTiempo:
                {
                    var vistas =
                        new RemoteViews(
                            _contexto.PackageName,
                            Resource.Layout.widget_fila_tiempo_aviso);

                    vistas.SetTextViewText(
                        Resource.Id.fila_tiempo_aviso_texto,
                        fila.Texto);

                    vistas.SetTextColor(
                        Resource.Id.fila_tiempo_aviso_texto,
                        global::Android.Graphics.Color.ParseColor(fila.Color));

                    SccosmosWidgetProvider.PonerClicItem(
                        vistas,
                        Resource.Id.fila_tiempo_aviso_raiz,
                        "abrir");

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
