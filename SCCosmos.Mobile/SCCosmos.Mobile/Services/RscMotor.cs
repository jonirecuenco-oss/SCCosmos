using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Services;

// =========================================================
// RSC · MOTOR DE FORMACIÓN DE GRUPOS (MÓVIL)
//
// Reglas:
//  · Solo entran las personas que asisten y salen a predicar.
//  · Los arreglos (con una o VARIAS personas) forman bloques:
//    todos los que van juntos viajan como una unidad.
//  · Si alguien no acepta grupo, su bloque no se fusiona:
//    sale solo con su arreglo (o solo/a).
//  · A ser posible, hombres con hombres y mujeres con mujeres:
//    se forman grupos por sexo y solo se mezclan si un lado
//    no llega al mínimo.
//  · Los grupos se forman de 6 y de 4. Si no cuadra, se admite
//    un grupo de 5 (o de 7). Nunca por debajo de 4, salvo los
//    bloques cerrados por arreglo.
//  · Se reparten las personas con territorio para que cada
//    grupo lleve al menos una, cuando es posible.
//
// Es el mismo motor que usa la versión de Windows.
// =========================================================

public static class RscMotor
{
    public const int MinimoGrupo = 4;

    private sealed class Bloque
    {
        public bool Cerrado { get; set; }

        public List<RscPersonaMobile> Miembros { get; } =
            new List<RscPersonaMobile>();

        /// <summary>Nombres de personas citadas que no salen hoy.</summary>
        public List<string> FueraNombres { get; } =
            new List<string>();

        public bool LlevaTerritorio =>
            Miembros.Any(m => m.TieneTerritorio);

        /// <summary>1 hombre, 2 mujer, 0 mezcla o sin dato.</summary>
        public int ClaseSexo
        {
            get
            {
                int clase = Miembros.Count == 0
                    ? 0
                    : Miembros[0].ClaseSexo;

                if (clase == 0)
                {
                    return 0;
                }

                return Miembros.All(m => m.ClaseSexo == clase)
                    ? clase
                    : 0;
            }
        }
    }


    public static List<RscGrupoMobile> FormarGrupos(
        IEnumerable<RscPersonaMobile> personas,
        out List<string> avisos,
        IDictionary<int, string>? nombres = null)
    {
        avisos = new List<string>();

        var salen = (personas ?? Enumerable.Empty<RscPersonaMobile>())
            .Where(p => p != null && p.Asiste && p.SaleAPredicar)
            .GroupBy(p => p.PersonaId)
            .Select(g => g.First())
            .ToList();

        var grupos = new List<RscGrupoMobile>();

        if (salen.Count == 0)
        {
            return grupos;
        }

        var porId = salen.ToDictionary(p => p.PersonaId);

        // Para poder escribir el nombre de la persona con la que
        // alguien tiene un arreglo, aunque no esté en la RSC.
        var nombresPorId =
            new Dictionary<int, string>();

        if (nombres != null)
        {
            foreach (var par in nombres)
            {
                nombresPorId[par.Key] = par.Value;
            }
        }

        foreach (var persona in salen)
        {
            nombresPorId[persona.PersonaId] =
                persona.Nombre;
        }

        var bloques = ConstruirBloques(
            salen,
            porId,
            nombresPorId);

        var sueltos = bloques
            .Where(b => !b.Cerrado)
            .ToList();

        var cerrados = bloques
            .Where(b => b.Cerrado)
            .ToList();

        FormarPorSexo(
            sueltos,
            grupos,
            avisos);

        // ---- Grupos cerrados (arreglos que van aparte) ----
        foreach (var bloque in cerrados
                     .OrderByDescending(b => b.Miembros.Count))
        {
            var grupo = new RscGrupoMobile
            {
                Notas = TextoCerrado(bloque)
            };

            foreach (var miembro in bloque.Miembros)
            {
                grupo.Miembros.Add(miembro);
            }

            grupos.Add(grupo);
        }

        Renumerar(grupos);

        return grupos;
    }


    /// <summary>
    /// Agrupa a los integrantes en bloques: los arreglos mutuos
    /// (de dos o más personas) van como una unidad; quien cita a
    /// alguien que no sale, queda cerrado con su nota.
    /// </summary>
    private static List<Bloque> ConstruirBloques(
        List<RscPersonaMobile> salen,
        IDictionary<int, RscPersonaMobile> porId,
        IDictionary<int, string> nombresPorId)
    {
        var bloques = new List<Bloque>();
        var usados = new HashSet<int>();

        foreach (var persona in salen)
        {
            if (usados.Contains(persona.PersonaId))
            {
                continue;
            }

            // Componente de arreglos mutuos (por anchura).
            var miembroIds = new HashSet<int>
            {
                persona.PersonaId
            };

            var cola = new Queue<RscPersonaMobile>();

            cola.Enqueue(persona);
            usados.Add(persona.PersonaId);

            while (cola.Count > 0)
            {
                var actual = cola.Dequeue();

                foreach (int citado in
                         actual.ArregloConPersonaIds
                             ?? new List<int>())
                {
                    if (!porId.TryGetValue(citado, out var otro)
                        || otro == null
                        || usados.Contains(otro.PersonaId))
                    {
                        continue;
                    }

                    // Solo cuenta si el vínculo es mutuo.
                    if (otro.ArregloConPersonaIds != null
                        && otro.ArregloConPersonaIds
                            .Contains(actual.PersonaId))
                    {
                        usados.Add(otro.PersonaId);
                        miembroIds.Add(otro.PersonaId);
                        cola.Enqueue(otro);
                    }
                }
            }

            var bloque = new Bloque();

            foreach (var miembro in salen.Where(
                         p => miembroIds.Contains(p.PersonaId)))
            {
                bloque.Miembros.Add(miembro);
            }

            // Personas citadas que no forman parte del bloque:
            // o no salen hoy, o el vínculo no es mutuo.
            foreach (var miembro in bloque.Miembros)
            {
                foreach (int citado in
                         miembro.ArregloConPersonaIds
                             ?? new List<int>())
                {
                    if (miembroIds.Contains(citado))
                    {
                        continue;
                    }

                    string nombre = nombresPorId.TryGetValue(
                        citado,
                        out string? encontrado)
                            ? encontrado
                            : "otro hermano";

                    if (!bloque.FueraNombres.Contains(nombre))
                    {
                        bloque.FueraNombres.Add(nombre);
                    }
                }
            }

            bloque.Cerrado =
                bloque.FueraNombres.Count > 0
                || bloque.Miembros.Any(m => !m.AceptaGrupo);

            bloques.Add(bloque);
        }

        return bloques;
    }


    /// <summary>
    /// Reparte los bloques sueltos en grupos por sexo (hombres
    /// con hombres, mujeres con mujeres) y mezcla solo cuando un
    /// lado no llega al mínimo.
    /// </summary>
    private static void FormarPorSexo(
        List<Bloque> sueltos,
        List<RscGrupoMobile> grupos,
        List<string> avisos)
    {
        if (sueltos.Count == 0)
        {
            return;
        }

        var hombres = sueltos
            .Where(b => b.ClaseSexo == 1)
            .ToList();

        var mujeres = sueltos
            .Where(b => b.ClaseSexo == 2)
            .ToList();

        var sinDato = sueltos
            .Where(b => b.ClaseSexo == 0)
            .ToList();

        int Total(List<Bloque> bloques) =>
            bloques.Sum(b => b.Miembros.Count);

        var pools = new List<List<Bloque>>();

        if (Total(hombres) >= MinimoGrupo
            && Total(mujeres) >= MinimoGrupo)
        {
            pools.Add(hombres);
            pools.Add(mujeres);
        }
        else if (Total(hombres) + Total(mujeres) > 0)
        {
            pools.Add(
                hombres
                    .Concat(mujeres)
                    .ToList());
        }

        if (sinDato.Count > 0)
        {
            if (Total(sinDato) >= MinimoGrupo
                || pools.Count == 0)
            {
                pools.Add(sinDato);
            }
            else
            {
                // Los bloques sin dato se añaden al pool más
                // pequeño y se recalcula todo junto.
                var menor = pools
                    .OrderBy(Total)
                    .First();

                menor.AddRange(sinDato);
            }
        }

        foreach (var pool in pools)
        {
            int total = Total(pool);

            var tamanos = CalcularTamanos(total);

            if (tamanos.Count == 0)
            {
                continue;
            }

            var gruposFusionados = tamanos
                .Select(t => new RscGrupoMobile())
                .ToList();

            Repartir(
                pool,
                gruposFusionados,
                tamanos,
                avisos);

            foreach (var grupo in gruposFusionados)
            {
                if (grupo.Miembros.Count == 0)
                {
                    continue;
                }

                grupos.Add(grupo);

                if (grupo.Miembros.Count < MinimoGrupo)
                {
                    avisos.Add(
                        "Un grupo ha quedado con "
                        + grupo.Miembros.Count
                        + " personas (mínimo "
                        + MinimoGrupo
                        + ").");
                }
            }
        }
    }


    private static string TextoCerrado(Bloque bloque)
    {
        if (bloque.FueraNombres.Count > 0)
        {
            return "Arreglo: solo con "
                + string.Join(", ", bloque.FueraNombres);
        }

        if (bloque.Miembros.Count == 1)
        {
            return "Va solo/a (no acepta grupo)";
        }

        return "Arreglo: van juntos ("
            + bloque.Miembros.Count + ")";
    }


    /// <summary>
    /// Reparte el total en grupos de 6 y 4; si no cuadra,
    /// admite un 5 (o un 7). Nunca por debajo de 4.
    /// </summary>
    public static List<int> CalcularTamanos(int total)
    {
        var resultado = new List<int>();

        if (total <= 0)
        {
            return resultado;
        }

        if (total < MinimoGrupo)
        {
            resultado.Add(total);
            return resultado;
        }

        const int infinito = 1000000;

        var coste = new int[total + 1];
        var previo = new int[total + 1];

        for (int i = 0; i <= total; i++)
        {
            coste[i] = infinito;
            previo[i] = -1;
        }

        coste[0] = 0;

        int[] opciones = { 6, 4, 5, 7 };

        for (int i = 1; i <= total; i++)
        {
            foreach (int tamano in opciones)
            {
                if (i - tamano < 0)
                {
                    continue;
                }

                if (coste[i - tamano] == infinito)
                {
                    continue;
                }

                int candidato =
                    coste[i - tamano]
                    + (tamano == 6 || tamano == 4
                        ? 0
                        : (tamano == 5 ? 2 : 4));

                if (candidato < coste[i])
                {
                    coste[i] = candidato;
                    previo[i] = tamano;
                }
            }
        }

        if (coste[total] == infinito)
        {
            resultado.Add(total);
            return resultado;
        }

        int resto = total;

        while (resto > 0)
        {
            int tamano = previo[resto];

            if (tamano <= 0)
            {
                resultado.Clear();
                resultado.Add(total);
                return resultado;
            }

            resultado.Add(tamano);
            resto -= tamano;
        }

        resultado.Sort();
        resultado.Reverse();

        return resultado;
    }


    private static void Repartir(
        List<Bloque> bloques,
        List<RscGrupoMobile> grupos,
        List<int> tamanos,
        List<string> avisos)
    {
        if (grupos.Count == 0 || bloques.Count == 0)
        {
            return;
        }

        int Hueco(int indice) =>
            tamanos[indice] - grupos[indice].Miembros.Count;

        void Colocar(Bloque bloque, bool buscarGrupoSinTerritorio)
        {
            var candidatos = Enumerable
                .Range(0, grupos.Count)
                .Where(i => Hueco(i) >= bloque.Miembros.Count)
                .ToList();

            int elegido;

            if (candidatos.Count == 0)
            {
                // No cabe con holgura: al grupo con más hueco.
                elegido = Enumerable
                    .Range(0, grupos.Count)
                    .OrderByDescending(Hueco)
                    .First();

                if (bloque.Miembros.Count > 0)
                {
                    avisos.Add(
                        "No se pudo encajar a "
                        + bloque.Miembros[0].Nombre
                        + " respetando el tamaño ideal.");
                }
            }
            else if (buscarGrupoSinTerritorio)
            {
                var sinTerritorio = candidatos
                    .Where(i => !grupos[i].Miembros
                        .Any(m => m.TieneTerritorio))
                    .ToList();

                elegido = sinTerritorio.Count > 0
                    ? sinTerritorio
                        .OrderByDescending(Hueco)
                        .First()
                    : candidatos
                        .OrderByDescending(Hueco)
                        .First();
            }
            else
            {
                elegido = candidatos
                    .OrderBy(Hueco)
                    .First();
            }

            foreach (var miembro in bloque.Miembros)
            {
                grupos[elegido].Miembros.Add(miembro);
            }
        }

        // Primero las personas con territorio, repartidas.
        foreach (var bloque in bloques
                     .Where(b => b.LlevaTerritorio)
                     .OrderByDescending(b => b.Miembros.Count))
        {
            Colocar(bloque, true);
        }

        foreach (var bloque in bloques
                     .Where(b => !b.LlevaTerritorio)
                     .OrderByDescending(b => b.Miembros.Count))
        {
            Colocar(bloque, false);
        }
    }


    /// <summary>Renumera los grupos 1..N y actualiza a sus miembros.</summary>
    public static void Renumerar(List<RscGrupoMobile> grupos)
    {
        int numero = 1;

        foreach (var grupo in grupos)
        {
            grupo.Numero = numero;

            foreach (var miembro in grupo.Miembros)
            {
                miembro.GrupoNumero = numero;
            }

            numero++;
        }
    }
}
