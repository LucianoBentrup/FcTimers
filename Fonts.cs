using System;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;

namespace FcTimers;

// Fontes maiores para títulos e números, tiradas da fonte padrão do Dalamud (tem todos os acentos do português).
// As fontes do próprio jogo não têm "ã" e "õ", por isso não são usadas aqui.
public static class Fonts
{
    private static IFontHandle? heading;
    private static IFontHandle? big;

    public static void Init(IUiBuilder ui)
    {
        heading = ui.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(toolkit => toolkit.AddDalamudDefaultFont(18)));
        big = ui.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(toolkit => toolkit.AddDalamudDefaultFont(23)));
    }

    // Título de card, seção e aba.
    public static IDisposable Heading() => heading?.Push() ?? Nothing.Instance;

    // Números grandes, como o tempo de volta de um sub.
    public static IDisposable Big() => big?.Push() ?? Nothing.Instance;

    public static void Dispose()
    {
        heading?.Dispose();
        big?.Dispose();
    }

    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}
