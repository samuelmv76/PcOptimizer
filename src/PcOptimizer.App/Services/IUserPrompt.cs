namespace PcOptimizer.App.Services;

/// <summary>
/// Preguntas al usuario. Es una interfaz para que el ViewModel se pueda
/// probar sin abrir ventanas.
/// </summary>
public interface IUserPrompt
{
    /// <summary>Confirmacion de una accion que no se puede deshacer.</summary>
    bool ConfirmDestructive(string title, string message);

    /// <summary>Pregunta de si o no, sin tono de advertencia.</summary>
    bool Ask(string title, string message);
}
