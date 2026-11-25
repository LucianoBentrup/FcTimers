using Xunit;

namespace FcTimers.Tests;

public class FcIdentityTests
{
    [Fact]
    public void O_ID_da_FC_vale_mais_que_a_tag()
    {
        var config = new Configuration { FcId = 10, FcTag = "ABC" };

        Assert.False(FcIdentity.IsOtherFc(new CharacterRecord { FcId = 10, FcTag = "XYZ" }, config));
        Assert.True(FcIdentity.IsOtherFc(new CharacterRecord { FcId = 11, FcTag = "ABC" }, config));
    }

    [Fact]
    public void Sem_ID_a_tag_diferente_marca_outra_FC_mas_a_igual_nao_libera()
    {
        var config = new Configuration { FcTag = "ABC" };

        Assert.True(FcIdentity.IsOtherFc(new CharacterRecord { FcTag = "XYZ" }, config));
        Assert.Null(FcIdentity.IsOtherFc(new CharacterRecord { FcTag = "ABC" }, config));
        Assert.Null(FcIdentity.IsOtherFc(new CharacterRecord(), config));
    }

    [Fact]
    public void Cada_instalacao_ganha_um_ID_de_dono_diferente()
    {
        var first = FcIdentity.NewOwnerId();

        Assert.Equal(32, first.Length);
        Assert.NotEqual(first, FcIdentity.NewOwnerId());
    }
}
