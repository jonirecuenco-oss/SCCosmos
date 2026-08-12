using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace SCCosmos
{
    public static class DocumentoHelper
    {
        /// <summary>
        /// Recibe una ruta. Si es de Word, devuelve la ruta de un PDF nuevo utilizando automatización COM nativa (Late Binding).
        /// </summary>
        public static string ConvertirAPdfSiEsWord(string rutaOriginal)
        {
            // Solo convertimos si es un archivo de Word (.doc o .docx)
            if (!rutaOriginal.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) &&
                !rutaOriginal.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                return rutaOriginal; // Si es PDF u otra cosa, devolvemos el archivo original sin tocarlo
            }

            string rutaPdf = Path.ChangeExtension(rutaOriginal, ".pdf");

            // Si ya habíamos convertido este archivo y el PDF es más reciente, lo usamos directamente para ahorrar tiempo
            if (File.Exists(rutaPdf) && File.GetLastWriteTime(rutaPdf) >= File.GetLastWriteTime(rutaOriginal))
            {
                return rutaPdf;
            }

            dynamic appWord = null;
            dynamic docs = null; // Variable explícita para la colección Documents (CRUCIAL para liberar memoria)
            dynamic doc = null;

            try
            {
                // Constantes internas de Microsoft Word
                int wdExportFormatPDF = 17; // Formato PDF
                int wdDoNotSaveChanges = 0; // No guardar cambios en el Word original

                // Buscamos si el ordenador tiene Microsoft Word instalado
                Type wordType = Type.GetTypeFromProgID("Word.Application");
                if (wordType == null)
                {
                    MessageBox.Show("No se detectó Microsoft Word instalado en este equipo.\nEl documento se enviará en su formato original (.docx).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    return rutaOriginal;
                }

                // Creamos una instancia "invisible" de Word
                appWord = Activator.CreateInstance(wordType);
                appWord.Visible = false;
                appWord.ScreenUpdating = false;

                // Abrimos el documento en modo Solo Lectura
                docs = appWord.Documents;
                doc = docs.Open(rutaOriginal, ReadOnly: true);

                // Exportamos a PDF
                doc.ExportAsFixedFormat(rutaPdf, wdExportFormatPDF);

                return rutaPdf; // Devolvemos la ruta del nuevo PDF creado
            }
            catch (Exception ex)
            {
                // Si algo falla, avisamos pero no rompemos el programa, simplemente enviamos el Word original
                MessageBox.Show($"Hubo un problema al intentar convertir el archivo a PDF:\n{Path.GetFileName(rutaOriginal)}\n\nSe enviará en formato Word.\nError: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return rutaOriginal;
            }
            finally
            {
                // ========================================================
                // RUTINA DE LIMPIEZA AGRESIVA PARA EVITAR "WORD FANTASMAS"
                // ========================================================
                try
                {
                    if (doc != null)
                    {
                        doc.Close(0); // 0 = wdDoNotSaveChanges
                        Marshal.ReleaseComObject(doc);
                    }

                    if (docs != null)
                    {
                        Marshal.ReleaseComObject(docs);
                    }

                    if (appWord != null)
                    {
                        appWord.Quit(0); // 0 = wdDoNotSaveChanges
                        Marshal.ReleaseComObject(appWord);
                    }
                }
                catch { }
                finally
                {
                    doc = null;
                    docs = null;
                    appWord = null;

                    // Forzar al recolector de basura de Windows a "matar" los procesos residuales en la RAM
                    // Hay que hacerlo doblemente porque los objetos COM tardan en soltarse
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
        }
    }
}