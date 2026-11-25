using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace FcTimers;

// Ícones dos itens tirados do próprio jogo. O Dalamud guarda as texturas, e o id do ícone de cada item fica em memória.
public static class GameIcons
{
    private static ITextureProvider? textures;
    private static IDataManager? data;
    private static readonly Dictionary<uint, uint> IconOfItem = [];

    public static void Init(ITextureProvider textureProvider, IDataManager dataManager)
    {
        textures = textureProvider;
        data = dataManager;
    }

    public static string ItemName(uint itemId)
        => itemId != 0 && data?.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } item ? item.Name.ToString() : string.Empty;

    // Desenha o ícone do item. Sem ícone (item 0 ou textura ainda carregando), ocupa o mesmo espaço vazio,
    // para a linha não pular quando a textura chegar.
    public static void Item(uint itemId, float size)
    {
        var icon = IconOf(itemId);
        if (icon != 0 && textures != null && textures.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrDefault() is { } wrap)
            ImGui.Image(wrap.Handle, new Vector2(size));
        else
            ImGui.Dummy(new Vector2(size));
    }

    private static uint IconOf(uint itemId)
    {
        if (itemId == 0 || data == null)
            return 0;

        if (!IconOfItem.TryGetValue(itemId, out var icon))
        {
            icon = data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Icon ?? 0u;
            IconOfItem[itemId] = icon;
        }

        return icon;
    }
}
