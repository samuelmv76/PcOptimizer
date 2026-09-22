namespace PcOptimizer.App.Services;

/// <summary>
/// Preguntas al usuario. Es una interfaz para que el ViewModel se pueda
/// probar sin abrir ventanas.
/// </summary>
public interface IUserPrompt
{
    /// <summary>Confirmacion de una accion que no se puede deshacer.</summary>
    bool ConfirmDestructive(string title, string message);
<<<<<<< HEAD

    /// <summary>Pregunta de si o no, sin tono de advertencia.</summary>
    bool Ask(string title, string message);
=======
>>>>>>> 77a6a47fbf3cb9b7c8cc565933bd42c37265aab0
}
