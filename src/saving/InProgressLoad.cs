using System;
using System.Diagnostics;
using Godot;

/// <summary>
///   Holds data needed for an in-progress load action.
///   And manages stepping through all the actions that need to happen
/// </summary>
/// <remarks>
///   <para>
///     This is very similar to InProgressSave
///   </para>
/// </remarks>
public class InProgressLoad
{
    private readonly string saveName;
    private State state = State.Initial;
    private Save save;

    private PackedScene loadedScene;
    private ILoadableGameState loadedState;

    private Stopwatch stopwatch;

    private bool success;
    private string message;
    private string exception;

    public InProgressLoad(string saveName)
    {
        this.saveName = saveName;
        stopwatch = Stopwatch.StartNew();
    }

    private enum State
    {
        Initial,
        ReadingData,
        ProcessingLoadedObjects,
        CreatingScene,
        Finished,
    }

    public void ReportStatus(bool success, string message, string exception = "")
    {
        this.success = success;
        this.message = message;
        this.exception = exception;
    }

    public void Start()
    {
        SceneManager.Instance.DetachCurrentScene();
        SceneManager.Instance.GetTree().Paused = true;

        Invoke.Instance.Perform(Step);
    }

    private void Step()
    {
        switch (state)
        {
            case State.Initial:
                state = State.ReadingData;
                LoadingScreen.Instance.Show(TranslationServer.Translate("LOAD_LOAD_GAME"),
                    TranslationServer.Translate("LOAD_READING_SAVE"));

                // Start suppressing loaded node deletion
                TemporaryLoadedNodeDeleter.Instance.AddDeletionHold(Constants.DELETION_HOLD_LOAD);

                break;
            case State.ReadingData:
            {
                // TODO: do this in a background thread if possible
                try
                {
                    save = Save.LoadFromFile(saveName, () => Invoke.Instance.Perform(() =>
                        LoadingScreen.Instance.Show(TranslationServer.Translate("LOAD_LOAD_GAME"),
                            TranslationServer.Translate("LOAD_SAVE_OBJECTS"))));
                }
                catch (Exception e)
                {
                    ReportStatus(false,
                        TranslationServer.Translate("LOAD_SAVE_EXC"),
                        e.ToString());
                    state = State.Finished;
                    break;
                }

                state = State.CreatingScene;
                break;
            }

            case State.CreatingScene:
            {
                try
                {
                    loadedScene = SceneManager.Instance.LoadScene(save.GameState);
                }
                catch (ArgumentException)
                {
                    ReportStatus(false, TranslationServer.Translate("LOAD_SAVE_INVALID"),
                        TranslationServer.Translate("LOAD_SAVE_UNKNOWN_STATE"));
                    state = State.Finished;
                    break;
                }

                try
                {
                    loadedState = (ILoadableGameState)loadedScene.Instance();
                }
                catch (Exception e)
                {
                    ReportStatus(false,
                        TranslationServer.Translate("LOAD_SCENE_EXC"),
                        e.ToString());
                    state = State.Finished;
                    break;
                }

                state = State.ProcessingLoadedObjects;
                break;
            }

            case State.ProcessingLoadedObjects:
            {
                LoadingScreen.Instance.Show(TranslationServer.Translate("LOAD_LOAD_GAME"),
                    TranslationServer.Translate("LOAD_SAVE_PROC_OBJ"));

                loadedState.IsLoadedFromSave = true;

                SceneManager.Instance.SwitchToScene(loadedState.GameStateRoot);

                try
                {
                    loadedState.OnFinishLoading(save);
                }
                catch (Exception e)
                {
                    ReportStatus(false,
                        TranslationServer.Translate("LOAD_SAVE_PROC_OBJ_EXC"),
                        e.ToString());
                    state = State.Finished;
                    break;
                }

                ReportStatus(true, TranslationServer.Translate("LOAD_FINISHED"), string.Empty);
                state = State.Finished;
                break;
            }

            case State.Finished:
            {
                stopwatch.Stop();
                GD.Print("load finished, success: ", success, " message: ", message, " elapsed: ", stopwatch.Elapsed);

                // Stop suppressing loaded node deletion
                TemporaryLoadedNodeDeleter.Instance.RemoveDeletionHold(Constants.DELETION_HOLD_LOAD);

                if (success)
                {
                    LoadingScreen.Instance.Hide();
                    SaveStatusOverlay.Instance.ShowMessage(message);

                    loadedState.GameStateRoot.GetTree().Paused = false;
                }
                else
                {
                    SaveStatusOverlay.Instance.ShowError(TranslationServer.Translate("LOAD_ERROR"),
                        message, exception, true,
                        () => LoadingScreen.Instance.Hide());
                }

                return;
            }

            default:
                throw new InvalidOperationException();
        }

        Invoke.Instance.Queue(Step);
    }
}
