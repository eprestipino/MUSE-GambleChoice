using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace GambleChoice
{
    // Unity/M-USE adapter. Economics and trial timing remain in the pure engine.
    public sealed class GambleRuntime : MonoBehaviour
    {
        public GambleEngine Engine
        {
            get; private set;
        }
        public GambleView View
        {
            get; private set;
        }
        public string OutputPath
        {
            get; private set;
        }
        public string Error
        {
            get; private set;
        }
        public Action<GambleResult> Completed;
        public Action<GambleEvent> EventObserved;
        public bool Integrated;
        public double Now => clock.Elapsed.TotalMilliseconds;

        readonly Stopwatch clock = Stopwatch.StartNew();
        StreamWriter journal, frames;
        bool finalized, armed, handlingFailure, failureLogged;
        int finger = -1;
        Coroutine rewardRoutine;
        int validationChoice = -1;
        double validationUntil;
        GameObject ownedCanvas;

        public void Initialize(string directory, Camera camera, bool integrated, Canvas taskCanvas = null)
        {
            if (Engine != null)
                throw new InvalidOperationException("Runtime already initialized.");
            if (integrated && taskCanvas == null)
                throw new ArgumentException("Integrated GambleChoice requires its M-USE scene canvas.");

            Integrated = integrated;
            OutputPath = Path.Combine(directory, "GambleChoice_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")
                + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(OutputPath);
                journal = new StreamWriter(Path.Combine(OutputPath, "events.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true };
                frames = new StreamWriter(Path.Combine(OutputPath, "frames.tsv"), false, new UTF8Encoding(false));
                frames.WriteLine("TrialId\tFrame\tTimeMs\tPhase\tPointerX\tPointerY\tHeld\tTarget\tHoldingIndex");
                File.WriteAllText(Path.Combine(OutputPath, "session.json"), JsonConvert.SerializeObject(new
                {
                    SchemaVersion = 1,
                    UtcStart = DateTime.UtcNow,
                    UnityVersion = Application.unityVersion,
                    Integrated = integrated,
                    ScreenWidth = Screen.width,
                    ScreenHeight = Screen.height,
                    Clock = "Stopwatch",
                    ClockFrequency = Stopwatch.Frequency,
                    OnsetPolicy = "After model preparation; software frame timing, not measured scanout",
                    PointerPolicy = "InputBroker mouse/single native touch; exact task button bounds; no shotgun tolerance",
                    RandomAlgorithm = GambleRandom.Algorithm,
                    PaletteCalibration = "Provisional RGB; not measured",
                    BuildGuid = Application.buildGUID
                }, Formatting.Indented));

                Canvas canvas = taskCanvas;
                if (canvas == null)
                {
                    ownedCanvas = new GameObject("GambleDemo_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                    ownedCanvas.transform.SetParent(transform, false);
                    canvas = ownedCanvas.GetComponent<Canvas>();
                    canvas.renderMode = camera == null ? RenderMode.ScreenSpaceOverlay : RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 1;
                    canvas.sortingOrder = 20;
                }

                var graphic = new GameObject("GambleChoice_LotteryDisplay", typeof(RectTransform), typeof(CanvasRenderer), typeof(GambleView));
                graphic.layer = canvas.gameObject.layer;
                graphic.transform.SetParent(canvas.transform, false);
                var rect = graphic.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                View = graphic.GetComponent<GambleView>();
                View.raycastTarget = false;
                Engine = new GambleEngine();
                View.Engine = Engine;
                Engine.Event = ObserveEvent;
                Engine.Reward = Deliver;
                SetVisible(false);
            }
            catch (Exception exception)
            {
                Error = exception.ToString();
                journal?.Dispose();
                frames?.Dispose();
                journal = frames = null;
                if (View != null)
                    Destroy(View.gameObject);
                if (ownedCanvas != null)
                    Destroy(ownedCanvas);
                throw;
            }
        }

        void ObserveEvent(GambleEvent record)
        {
            record.Frame = Time.frameCount;
            Write(new
            {
                kind = "event",
                data = record
            });
            EventObserved?.Invoke(record);
            if (record.Type == "OutcomeResolved" || record.Type == "RewardRequested"
                || record.Type == "RewardSettled" || record.Type == "LedgerSettled")
                Write(new
                {
                    kind = "checkpoint",
                    data = Engine.Result
                });
        }

        public void Begin(GambleConfig config, int index, int block = 0, int planTrial = -1)
        {
            BeginPrepared(new GamblePreparedProtocol(config), index, block, planTrial);
        }

        public void BeginPrepared(GamblePreparedProtocol protocol, int index, int block = 0, int planTrial = -1)
        {
            if (Engine == null)
                throw new InvalidOperationException("Runtime is not initialized.");
            if (Error != null)
                throw new InvalidOperationException(Error);
            if (Engine.Result != null && !Engine.Done)
                throw new InvalidOperationException("Trial already running.");
            if (protocol == null)
                throw new ArgumentNullException(nameof(protocol));
            if (protocol.PhysicalReward && (!Integrated || Session.SyncBoxController == null
                || Session.SessionDef == null || !Session.SessionDef.SyncBoxActive))
                throw new InvalidOperationException("Physical reward requested without an active M-USE SyncBox.");
            finalized = armed = false;
            finger = validationChoice = -1;
            SetVisible(false);

            try
            {
                Engine.BeginPrepared(protocol, index, Now, block, planTrial, () => Now);
                if (InputBroker.isSimulation)
                    Engine.Result.InputSource = "MUSEInputBrokerSimulation";
                Write(new
                {
                    kind = "prepared",
                    data = Engine.Result
                });
                View.Now = Now;
                View.Refresh();
                // Funding stops must not present an unfunded offer for a frame.
                SetVisible(!Engine.Done);
                FinalizeIfDone();
            }
            catch (Exception exception) { Fail(exception); }
        }

        public void SetVisible(bool visible)
        {
            if (View != null)
                View.gameObject.SetActive(visible);
        }

        void Write(object record)
        {
            if (journal == null)
                throw new InvalidOperationException("GambleChoice journal is unavailable.");
            journal.WriteLine(JsonConvert.SerializeObject(record));
        }

        void Update()
        {
            if (Engine?.Result == null || Engine.Done)
                return;
            if (Integrated && Time.timeScale == 0)
            {
                Abort("MUSEPaused");
                return;
            }
            try
            {
                Vector2 pointer = InputBroker.mousePosition;
                bool down = InputBroker.GetMouseButtonDown(0);
                // The broker's simulated DOWN state is distinct from PRESSED.
                bool held = InputBroker.GetMouseButton(0) || down;
                if (!InputBroker.isSimulation && Input.touchCount > 0)
                {
                    // Never transfer an active hold to a second finger.
                    if (finger < 0 && Input.GetTouch(0).phase == TouchPhase.Began)
                        finger = Input.GetTouch(0).fingerId;
                    held = down = false;
                    for (int i = 0; i < Input.touchCount; i++)
                    {
                        var touch = Input.GetTouch(i);
                        if (touch.fingerId != finger)
                            continue;
                        pointer = touch.position;
                        held = touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled;
                        down = touch.phase == TouchPhase.Began;
                    }
                }
                else
                    finger = -1;
                StepPointer(pointer, held, down, Now);
            }
            catch (Exception exception) { Fail(exception); }
        }

        // Shared by broker/native polling and explicitly labelled software replay.
        void StepPointer(Vector2 pointer, bool held, bool down, double now)
        {
            if (!held)
                armed = true;
            int hit = View.Hit(pointer);
            if (armed && down)
                Engine.Press(hit, now);
            if (validationChoice >= 0)
            {
                hit = validationChoice;
                held = now <= validationUntil;
                armed = true;
                if (!held)
                    validationChoice = -1;
            }
            Engine.Tick(now, hit, armed && held);
            View.Now = now;
            View.Refresh();
            frames.WriteLine(string.Join("\t", Engine.Result.TrialId, Time.frameCount,
                now.ToString("F3", CultureInfo.InvariantCulture), Engine.Phase,
                pointer.x.ToString(CultureInfo.InvariantCulture), pointer.y.ToString(CultureInfo.InvariantCulture),
                held, hit, Engine.HoldingIndex));
            FinalizeIfDone();
        }

        public void ValidationHold(int choice)
        {
            if (Integrated)
                throw new InvalidOperationException("Validation injection is forbidden in integrated sessions.");
            if (Engine?.Result == null || Engine.Phase != GamblePhase.Offer)
                return;
            Engine.Result.InputSource = "ValidationInjected";
            validationChoice = choice;
            validationUntil = Now + Engine.Result.Config.HoldMs + 150;
            Engine.Press(choice, Now);
        }

        void Deliver(int pulses)
        {
            if (!Engine.Result.Config.PhysicalReward)
            {
                Engine.RewardCompleted(Now, "Simulated");
                return;
            }
            rewardRoutine = StartCoroutine(PhysicalReward(pulses, Engine.Result.Config.PulseTicks, Engine.Result.TrialId));
        }

        IEnumerator PhysicalReward(int pulses, int ticks, string trialId)
        {
            yield return Session.SyncBoxController.SendRewardPulses(pulses, ticks);
            if (Engine.Result.TrialId == trialId && !Engine.Done)
                Engine.RewardCompleted(Now, "CommandsQueuedNotMeasured");
            rewardRoutine = null;
        }

        void StopRewardRoutine()
        {
            if (rewardRoutine == null)
                return;
            StopCoroutine(rewardRoutine);
            rewardRoutine = null;
        }

        void Fail(Exception exception)
        {
            if (handlingFailure)
                return;
            handlingFailure = true;
            try
            {
                if (Error == null) Error = exception.ToString();
                StopRewardRoutine();
                if (!failureLogged)
                {
                    failureLogged = true;
                    UnityEngine.Debug.LogError("GambleChoice stopped: " + Error);
                }
                if (Engine?.Result != null && !finalized)
                {
                    // A failed event sink must not prevent CancelHold/Abort from reaching Finished.
                    Engine.Event = null;
                    Engine.Abort(Now, "RuntimeOrDataFailure");
                    try
                    {
                        Write(new
                        {
                            kind = "fault",
                            data = Engine.Result
                        });
                    }
                    catch { }
                    try
                    {
                        FinalizeIfDone();
                    }
                    catch { /* Completion runs in FinalizeIfDone's finally. */ }
                }
                SetVisible(false);
                enabled = false;
            }
            finally { handlingFailure = false; }
        }

        public void Abort(string reason)
        {
            StopRewardRoutine();
            if (Engine?.Result == null)
                return;
            try
            {
                Engine.Abort(Now, reason);
                FinalizeIfDone();
            }
            catch (Exception exception) { Fail(exception); }
            finally { SetVisible(false); }
        }

        void FinalizeIfDone()
        {
            if (!Engine.Done || finalized)
                return;
            try
            {
                Write(new
                {
                    kind = "trial",
                    data = Engine.Result
                });
                frames.Flush();
            }
            catch (Exception exception)
            {
                // M-USE's completion callback must see the failure and stop this task.
                if (Error == null) Error = exception.ToString();
                StopRewardRoutine();
                Engine.Result.Status = "Aborted";
                Engine.Result.Reason = "DataWriteFailure";
                try
                {
                    Write(new
                    {
                        kind = "fault",
                        data = Engine.Result
                    });
                }
                catch { }
                throw;
            }
            finally
            {
                finalized = true;
                Completed?.Invoke(Engine.Result);
            }
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused)
                Abort("FocusLost");
        }
        void OnApplicationPause(bool paused)
        {
            if (paused)
                Abort("ApplicationPaused");
        }
        void OnDisable()
        {
            Abort("Disabled");
        }
        void OnDestroy()
        {
            Abort("SceneUnloaded");
            journal?.Dispose();
            frames?.Dispose();
            journal = frames = null;
            if (View != null && ownedCanvas == null)
            {
                if (Application.isPlaying)
                    Destroy(View.gameObject);
                else
                    DestroyImmediate(View.gameObject);
            }
        }
    }
}
