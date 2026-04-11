namespace ProjectLens.Application.Abstractions;

public interface IModelClientFactory
{
    IModelClient? Create(ModelProviderSettings settings);
}
