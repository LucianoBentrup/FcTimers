using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FcTimers;

// Registro do que o plugin viu, para conferir quando o loot ou a rega não batem.
// Fica em diagnostico.txt na pasta de configuração do plugin e nunca guarda a chave da FC.
public sealed class DiagnosticLog
{
    private const int MaxLines = 500;

    private readonly string path;
    private readonly Action<string> echo;
    private readonly Func<bool> detailed;
    private readonly List<string> lines = [];

    public DiagnosticLog(string path, Action<string> echo, Func<bool> detailed)
    {
        this.path = path;
        this.echo = echo;
        this.detailed = detailed;
        try
        {
            if (File.Exists(path))
                lines.AddRange(File.ReadAllLines(path).TakeLast(MaxLines));
        }
        catch (IOException)
        {
        }
    }

    // Quantas linhas foram escritas desde que o plugin abriu, para saber se há novidade para enviar.
    public int Written { get; private set; }

    public void Write(string message)
    {
        var line = $"{DateTime.Now:dd/MM HH:mm:ss} {message}";
        lines.Add(line);
        Written++;
        echo(message);

        try
        {
            // O arquivo só é reescrito de vez em quando, para não crescer sem limite.
            if (lines.Count >= MaxLines * 2)
            {
                lines.RemoveRange(0, lines.Count - MaxLines);
                File.WriteAllLines(path, lines);
            }
            else
            {
                File.AppendAllLines(path, [line]);
            }
        }
        catch (IOException)
        {
        }
    }

    // Só registra com o log detalhado ligado.
    public void Detail(string message)
    {
        if (detailed())
            Write(message);
    }

    public string Text(string header) => string.Join('\n', lines.TakeLast(MaxLines).Prepend(header));

    public static string Time(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd/MM HH:mm:ss");
}
