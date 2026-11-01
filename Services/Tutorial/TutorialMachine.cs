namespace ClaudeBarWin.Services.Tutorial;

/// <summary>Dónde está el tutorial: en un paso, en el cierre de un capítulo (lo aprendido y, sin Pro, el enlace) o al final.</summary>
public enum TutorialPhase { Step, ChapterEnd, Finished }

/// <summary>Cómo quedó un paso: sin hacer, hecho (se detectó su gesto) o saltado.</summary>
public enum TutorialStepStatus { Pending, Done, Skipped }

/// <summary>Qué hizo un gesto en el paso actual: nada, cambiar la demostración o completar el paso.</summary>
public enum TutorialOutcome { Ignored, Changed, Completed }

/// <summary>
/// La máquina de pasos del tutorial. PURA (sin WinForms ni reloj): la ventana le pasa los gestos ya traducidos
/// (<see cref="Handle"/>) y las órdenes del pie (<see cref="Next"/>, <see cref="Skip"/>, <see cref="Back"/>, <see cref="GoTo"/>).
/// <list type="bullet">
/// <item>Cada paso empieza con su notch de demostración (<see cref="TutorialStepInfo.Stage"/>): volver a él, con Atrás o
/// saltando de capítulo, lo deja como al principio, pero sigue hecho.</item>
/// <item>Un gesto que completa el paso lo marca hecho (<see cref="TutorialOutcome.Completed"/>); los demás solo mueven la
/// demostración, como haría el notch de verdad.</item>
/// <item>«Siguiente» solo avanza con el paso hecho o saltado; «Saltar paso» lo marca saltado y avanza.</item>
/// <item>Tras el último paso de un capítulo, su cierre (<see cref="TutorialPhase.ChapterEnd"/>): entonces el capítulo cuenta
/// como hecho (<see cref="ChaptersDone"/>, lo que se guarda en la config) y avisa con <see cref="ProgressChanged"/>.</item>
/// <item>Retomar: se empieza en el primer capítulo sin hacer (todos hechos: desde el principio).</item>
/// </list>
/// </summary>
public sealed class TutorialMachine
{
    private readonly Dictionary<TutorialStep, TutorialStepStatus> _status = new();
    private readonly HashSet<TutorialChapter> _done;
    private IReadOnlyList<TutorialStep> _steps;

    /// <param name="chaptersDone">Capítulos ya hechos (de la config), para retomar.</param>
    public TutorialMachine(IEnumerable<TutorialChapter>? chaptersDone = null)
    {
        _done = new HashSet<TutorialChapter>(chaptersDone ?? Array.Empty<TutorialChapter>());
        Chapter = TutorialScript.Chapters.FirstOrDefault(c => !_done.Contains(c), TutorialScript.Chapters[0]);
        _steps = TutorialScript.StepsOf(Chapter);
        Enter(0);
    }

    /// <summary>Un capítulo pasó a hecho (para guardarlo).</summary>
    public event Action? ProgressChanged;

    public TutorialPhase Phase { get; private set; } = TutorialPhase.Step;
    public TutorialChapter Chapter { get; private set; }

    /// <summary>Índice del paso dentro de su capítulo (en el cierre, el último).</summary>
    public int StepIndex { get; private set; }

    public TutorialStep Step => _steps[StepIndex];
    public TutorialStepInfo Info => TutorialScript.Info(Step);

    /// <summary>El notch de demostración ahora.</summary>
    public TutorialDemo Demo { get; private set; } = new();

    /// <summary>Capítulos hechos (lo que se guarda).</summary>
    public IReadOnlyCollection<TutorialChapter> ChaptersDone => _done;

    /// <summary>Pasos del capítulo actual.</summary>
    public IReadOnlyList<TutorialStep> Steps => _steps;

    /// <summary>1..N del capítulo y del paso, y cuántos hay (para «Capítulo 3 de 10 · Paso 2 de 4»).</summary>
    public int ChapterNumber => (int)Chapter + 1;
    public static int ChapterCount => TutorialScript.Chapters.Count;
    public int StepNumber => StepIndex + 1;
    public int StepCount => _steps.Count;

    public TutorialStepStatus Status(TutorialStep step) => _status.GetValueOrDefault(step);

    /// <summary>¿Está hecho (o saltado) el paso actual? Entonces «Siguiente» avanza.</summary>
    public bool CanAdvance => Phase != TutorialPhase.Step || Status(Step) != TutorialStepStatus.Pending;

    public bool StepDone => Phase == TutorialPhase.Step && Status(Step) == TutorialStepStatus.Done;

    public bool CanGoBack => Phase == TutorialPhase.Finished || StepIndex > 0 || Phase == TutorialPhase.ChapterEnd
        || Chapter != TutorialScript.Chapters[0];

    public bool IsChapterDone(TutorialChapter c) => _done.Contains(c);

    /// <summary>Un gesto sobre la demostración: la mueve como el notch de verdad y, si es el del paso, lo completa.</summary>
    public TutorialOutcome Handle(TutorialInput input)
    {
        if (Phase != TutorialPhase.Step || input.Act == TutorialAct.None) return TutorialOutcome.Ignored;
        var before = Demo;
        var after = before.Apply(input);
        Demo = after;
        if (Status(Step) != TutorialStepStatus.Done && TutorialScript.Completes(Step, input, before, after))
        {
            _status[Step] = TutorialStepStatus.Done;
            return TutorialOutcome.Completed;
        }
        return after == before ? TutorialOutcome.Ignored : TutorialOutcome.Changed;
    }

    /// <summary>La alternativa de teclado del paso (<see cref="TutorialScript.KeyInput"/>), como si fuera el gesto.</summary>
    public TutorialOutcome HandleKey(TutorialKey key) =>
        Phase == TutorialPhase.Step && TutorialScript.KeyInput(Step, Demo, key) is { } input ? Handle(input) : TutorialOutcome.Ignored;

    /// <summary>¿Hace algo esa tecla en este paso? (si no, ← → mueven entre pasos).</summary>
    public bool KeyActs(TutorialKey key) => Phase == TutorialPhase.Step && TutorialScript.KeyInput(Step, Demo, key) is not null;

    /// <summary>«Siguiente»: con el paso hecho o saltado, al siguiente; tras el último, el cierre del capítulo; tras el cierre,
    /// el capítulo siguiente o el final. False si no se movió.</summary>
    public bool Next()
    {
        switch (Phase)
        {
            case TutorialPhase.Step:
                if (!CanAdvance) return false;
                if (StepIndex + 1 < _steps.Count) Enter(StepIndex + 1);
                else EndChapter();
                return true;
            case TutorialPhase.ChapterEnd:
                if ((int)Chapter + 1 < ChapterCount) GoTo(Chapter + 1);
                else Phase = TutorialPhase.Finished;
                return true;
            default:
                return false;
        }
    }

    /// <summary>«Saltar paso»: queda saltado (si no estaba hecho) y avanza. En el cierre, como «Siguiente».</summary>
    public bool Skip()
    {
        if (Phase == TutorialPhase.Step && Status(Step) == TutorialStepStatus.Pending) _status[Step] = TutorialStepStatus.Skipped;
        return Next();
    }

    /// <summary>«Atrás»: el paso anterior (cruzando de capítulo), desde el cierre el último paso, desde el final el último
    /// cierre. El paso vuelve a su demostración inicial. False si ya está en el primero.</summary>
    public bool Back()
    {
        switch (Phase)
        {
            case TutorialPhase.Finished:
                Phase = TutorialPhase.ChapterEnd;
                return true;
            case TutorialPhase.ChapterEnd:
                Enter(_steps.Count - 1);
                return true;
        }
        if (StepIndex > 0)
        {
            Enter(StepIndex - 1);
            return true;
        }
        if (Chapter == TutorialScript.Chapters[0]) return false;
        Chapter--;
        _steps = TutorialScript.StepsOf(Chapter);
        Enter(_steps.Count - 1);
        return true;
    }

    /// <summary>Al primer paso de un capítulo (clic en su marca del progreso).</summary>
    public void GoTo(TutorialChapter chapter)
    {
        if (!Enum.IsDefined(chapter)) return;
        Chapter = chapter;
        _steps = TutorialScript.StepsOf(chapter);
        Enter(0);
    }

    /// <summary>A un paso del capítulo actual (clic en su punto del pie): empieza con su demostración, como al llegar.</summary>
    public void GoToStep(int index)
    {
        if (index < 0 || index >= _steps.Count) return;
        Enter(index);
    }

    /// <summary>Otra vez desde el principio (los capítulos hechos siguen hechos).</summary>
    public void Restart() => GoTo(TutorialScript.Chapters[0]);

    private void Enter(int index)
    {
        Phase = TutorialPhase.Step;
        StepIndex = Math.Clamp(index, 0, _steps.Count - 1);
        Demo = TutorialScript.Info(Step).Stage;
    }

    private void EndChapter()
    {
        Phase = TutorialPhase.ChapterEnd;
        if (_done.Add(Chapter)) ProgressChanged?.Invoke();
    }

    /// <summary>Los capítulos hechos guardados en la config (por nombre; lo que no se reconoce, fuera).</summary>
    public static IReadOnlyList<TutorialChapter> Parse(IEnumerable<string>? names) =>
        (names ?? Array.Empty<string>())
            .Select(n => !string.IsNullOrEmpty(n) && n.All(char.IsAsciiLetter)
                && Enum.TryParse<TutorialChapter>(n, ignoreCase: false, out var c) && Enum.IsDefined(c) ? c : (TutorialChapter?)null)
            .Where(c => c is not null).Select(c => c!.Value).Distinct().OrderBy(c => c).ToArray();

    /// <summary>Lo que se guarda en la config: los nombres de los capítulos hechos, en orden.</summary>
    public static string[] Format(IEnumerable<TutorialChapter> chapters) =>
        chapters.Where(Enum.IsDefined).Distinct().OrderBy(c => c).Select(c => c.ToString()).ToArray();
}
