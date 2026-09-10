using AgentForge.Core.Chat;

namespace AgentForge.Core.Abstractions;

/// <summary>
/// Adapter para um provider de LLM (Anthropic, OpenAI, Bedrock, …).
/// Implementadores traduzem <see cref="ChatRequest"/> para o dialeto do provider
/// e mapeiam a resposta de volta pra <see cref="ChatResponse"/>.
/// </summary>
public interface IChatProvider
{
    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default);
}
