﻿using System.Linq;
using UniRx;
using UniRx.Triggers;
using UnityEngine;
using UnityEngine.UI;


public class NotesEditorPresenter : MonoBehaviour
{
    [SerializeField]
    AudioSource audioSource;
    [SerializeField]
    Button playButton;
    [SerializeField]
    Text titleText;
    [SerializeField]
    DrawLineTest drawLineTest;
    [SerializeField]
    Slider _scaleSliderTest;
    [SerializeField]
    Slider divisionNumOfOneMeasureSlider;
    [SerializeField]
    InputField BPMInputField;


    Subject<Vector3> OnMouseDownStream = new Subject<Vector3>();

    void Awake()
    {
        if (SelectedMusicDataStore.Instance.audioClip == null)
        {
            ObservableWWW.GetWWW("file:///" + Application.persistentDataPath + "/Musics/test.wav").Subscribe(www =>
            {
                SelectedMusicDataStore.Instance.audioClip = www.audioClip;
                Init();
            });

            return;
        }

        Init();
    }

    void Init()
    {
        var model = NotesEditorModel.Instance;
        var rectTransform = GetComponent<RectTransform>();
        var unitBeatSamples = new ReactiveProperty<int>();


        divisionNumOfOneMeasureSlider.OnValueChangedAsObservable()
            .Select(x => Mathf.FloorToInt(x))
            .Subscribe(x => model.DivisionNumOfOneMeasure.Value = x);


        // Apply music data
        audioSource.clip = SelectedMusicDataStore.Instance.audioClip;
        titleText.text = SelectedMusicDataStore.Instance.fileName ?? "Test";


        {   // Initialize canvas width
            var sizeDelta = rectTransform.sizeDelta;
            sizeDelta.x = audioSource.clip.samples / 100f;
            rectTransform.sizeDelta = sizeDelta;
        }


        // Canvas width scaler Test
        var canvasWidth = _scaleSliderTest.OnValueChangedAsObservable()
            .DistinctUntilChanged()
            .Select(x => audioSource.clip.samples / 100f * x)
            .Do(x => {
                var delta = rectTransform.sizeDelta;
                delta.x = x;
                rectTransform.sizeDelta = delta;
            }).ToReactiveProperty();


        // Binds BPM
        unitBeatSamples = model.BPM.DistinctUntilChanged()
            .Select(x => Mathf.FloorToInt(audioSource.clip.frequency * 60 / x))
            .ToReactiveProperty();

        BPMInputField.OnValueChangeAsObservable()
            .Select(x => string.IsNullOrEmpty(x) ? "1" : x)
            .Select(x => int.Parse(x))
            .Select(x => Mathf.Clamp(x, 1, 320))
            .Subscribe(x => model.BPM.Value = x);

        model.BPM.DistinctUntilChanged()
            .Subscribe(x => BPMInputField.text = x.ToString());


        // Binds canvas position from samples
        this.UpdateAsObservable()
            .Select(_ => audioSource.timeSamples)
            .DistinctUntilChanged()
            .Merge(canvasWidth.Select(_ => audioSource.timeSamples)) // Merge resized timing
            .Select(timeSamples => timeSamples / (float)audioSource.clip.samples)
            .Select(per => rectTransform.sizeDelta.x * per)
            .Subscribe(x => rectTransform.localPosition = Vector3.left * x);


        // Binds samples from dragging canvas
        var canvasDragStream = this.UpdateAsObservable()
            .SkipUntil(OnMouseDownStream)
            .TakeWhile(_ => !Input.GetMouseButtonUp(0))
            .Select(_ => Mathf.FloorToInt(Input.mousePosition.x));

        canvasDragStream.Zip(canvasDragStream.Skip(1), (p, c) => new { p, c })
            .RepeatSafe()
            .Select(b => (b.p - b.c) / canvasWidth.Value)
            .Select(p => Mathf.FloorToInt(audioSource.clip.samples * p))
            .Select(deltaSample => audioSource.timeSamples + deltaSample)
            .Select(x => Mathf.Clamp(x, 0, audioSource.clip.samples - 1))
            .Subscribe(x => audioSource.timeSamples = x);

        var isDraggingDuringPlay = false;
        OnMouseDownStream.Where(_ => model.IsPlaying.Value)
            .Select(_ => model.IsPlaying.Value = false)
            .Subscribe(_ => isDraggingDuringPlay = true);

        this.UpdateAsObservable().Where(_ => isDraggingDuringPlay)
            .Where(_ => Input.GetMouseButtonUp(0))
            .Select(_ => model.IsPlaying.Value = true)
            .Subscribe(_ => isDraggingDuringPlay = false);


        // Binds play pause toggle
        playButton.OnClickAsObservable()
            .Subscribe(_ => model.IsPlaying.Value = !model.IsPlaying.Value);

        model.IsPlaying.DistinctUntilChanged().Subscribe(playing => {
            var playButtonText = playButton.GetComponentInChildren<Text>();

            if (playing)
            {
                audioSource.Play();
                playButtonText.text = "Pause";
            }
            else
            {
                audioSource.Pause();
                playButtonText.text = "Play";
            }
        });


        // Draw lines
        this.UpdateAsObservable()
            .Select(_ => Enumerable.Range(0, Mathf.CeilToInt(audioSource.clip.samples / (float)unitBeatSamples.Value) * model.DivisionNumOfOneMeasure.Value)
                .Select(i => i * unitBeatSamples.Value / (float)audioSource.clip.samples / model.DivisionNumOfOneMeasure.Value)
                .Select(per => per * canvasWidth.Value)
                .Select(x => x - canvasWidth.Value * (audioSource.timeSamples / (float)audioSource.clip.samples))
                .Select((x, i) => new Line(new Vector3(x, 250, 0), new Vector3(x, -250, 0), i % model.DivisionNumOfOneMeasure.Value == 0 ? Color.white : Color.white / 2)))
            .Subscribe(lines => drawLineTest.DrawLines(lines.ToArray()));
    }

    public void OnMouseDown()
    {
        OnMouseDownStream.OnNext(Input.mousePosition);
    }
}
