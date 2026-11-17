namespace Messenger.Api.OpenApi;

/// <summary>
/// Помечает операцию, тело которой — поток зашифрованных байтов. Тип
/// содержимого клиента не проверяется, поэтому в спецификации указывается
/// бинарное тело без требования конкретного media type.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AttachmentUploadBodyAttribute : Attribute;