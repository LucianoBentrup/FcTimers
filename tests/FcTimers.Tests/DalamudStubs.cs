// O Configuration do plugin implementa esta interface do Dalamud. Nos testes basta uma cópia com o mesmo formato.
namespace Dalamud.Configuration;

public interface IPluginConfiguration
{
    int Version { get; set; }
}
