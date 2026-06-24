#region Область using
using LLama;
using LLama.Common;
using LLama.Exceptions;
using LLama.Native;
using LLama.Sampling;
using Microsoft.Extensions.AI;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using static LLama.LLamaTransforms;
using static System.Net.WebRequestMethods;
#endregion
namespace JustOneClick
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Свойства и поля
        // Список сообщений чата — привязан к ItemsControl в XAML
        public ObservableCollection<ChatMessage> Messages { get; } = [];

        // INotifyPropertyChanged — уведомляем UI об изменении свойств
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // Список найденных GGUF файлов — привязан к ComboBox
        public ObservableCollection<GgufModel> ModelFiles { get; } = [];

        private MtmdWeights? _mtmdWeights;  // clip/mmproj модель
        private string? _pendingImagePath; // путь к картинке для следующего сообщения


        // Выбранная модель в ComboBox
        private GgufModel? _selectedModel;
        public GgufModel? SelectedModel
        {
            get => _selectedModel;
            set { _selectedModel = value; Notify(nameof(SelectedModel)); }
        }

        // Текущий режим ИИ (название для тултипа кнопки 🎭)
        private string _activeModeName = "Ассистент";
        public string ActiveModeName
        {
            get => _activeModeName;
            set { _activeModeName = value; Notify(nameof(ActiveModeName)); }
        }

        // Системный промпт текущего режима
        public string ActiveSystemPrompt { get; private set; }
            = "Ты — универсальный помощник. Отвечай чётко, по делу. /NO THINK";

        // Путь к папке с моделями
        private string _modelsFolder = "";

        // ── LLamaSharp объекты ──
        private LLamaWeights? _llamaWeights;   // веса модели (файл .gguf)
        private LLamaContext? _llamaContext;   // контекст (память диалога)
        private ChatSession? _chatSession;    // сессия чата с историей

        // ── Флаги состояния ──
        private bool _modelReady;               // модель загружена и готова
        private bool _generating;               // идёт генерация ответа

        // Токен отмены генерации (нажатие кнопки Stop или смена модели)
        private CancellationTokenSource? _generationCts;

        // Результаты поиска по чату
        private List<int> _foundIndices = [];  // индексы сообщений
        private int _foundCurrent = -1;     // текущий выбранный

        #endregion

        #region Область для перетаскивания и ресайза

        // P/Invoke для ресайза
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private readonly IntPtr HT_LEFT = 10;
        private readonly IntPtr HT_RIGHT = 11;
        private readonly IntPtr HT_TOP = 12;
        private readonly IntPtr HT_BOTTOM = 15;
        private readonly IntPtr HT_TOPLEFT = 13;
        private readonly IntPtr HT_TOPRIGHT = 14;
        private readonly IntPtr HT_BOTTOMLEFT = 16;
        private readonly IntPtr HT_BOTTOMRIGHT = 17;

        #endregion

        public class GgufModel
        {
            public string FullPath { get; set; } = "";
            public string? MmprojPath { get; set; }

            public string DisplayName =>
                Path.GetFileNameWithoutExtension(FullPath)
                + (MmprojPath != null ? " 👁" : "");
        }

        public class ChatMessage : INotifyPropertyChanged
        {
            private string? _text;
            public string? Text
            {
                get => _text;
                set
                {
                    _text = value;
                    PropertyChanged?.Invoke(this,
                        new PropertyChangedEventArgs(nameof(Text)));
                }
            }

            public bool IsUser { get; set; }
            public DateTime Time { get; set; } = DateTime.Now;

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;
            
            // Двойной клик по header — разворачиваем/восстанавливаем
            this.MouseDoubleClick += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                    ToggleMaxRestore();
            };

            // Ctrl+F открывает поиск
            this.KeyDown += (s, e) =>
            {
                if (e.Key == Key.F &&
                    (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    BtnSearch_Click(s, new RoutedEventArgs());
                }
            };

        }

        #region Область обработчики кнопок и событий

        // Обработчики зон ресайза
        private void ResizeLeft_MouseDown(object sender, MouseButtonEventArgs e)
            => DoResize(HT_LEFT);
        private void ResizeRight_MouseDown(object sender, MouseButtonEventArgs e)
            => DoResize(HT_RIGHT);
        private void ResizeTop_MouseDown(object sender, MouseButtonEventArgs e)
            => DoResize(HT_TOP);
        private void ResizeBottom_MouseDown(object sender, MouseButtonEventArgs e)
            => DoResize(HT_BOTTOM);
        private void ResizeTopLeft_MouseDown(object sender, MouseButtonEventArgs e)
            => DoResize(HT_TOPLEFT);
        private void ResizeTopRight_MouseDown(object sender, MouseButtonEventArgs e)
            => DoResize(HT_TOPRIGHT);
        private void ResizeBottomLeft_MouseDown(object sender, MouseButtonEventArgs e)
            => DoResize(HT_BOTTOMLEFT);
        private void ResizeBottomRight_MouseDown(object sender, MouseButtonEventArgs e)
            => DoResize(HT_BOTTOMRIGHT);

        private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMax_Click(object sender, RoutedEventArgs e) => ToggleMaxRestore();

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove(); 
        }

        private void CopyMessage_Executed(object sender, ExecutedRoutedEventArgs e)
        {

            if (e.Parameter is string txt)
                Clipboard.SetText(txt);

        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e) => ToggleSearchPanel();

        private async void Send_Click(object sender, RoutedEventArgs e) => await SendUserMessageAsync();

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _generationCts?.Cancel();
            ReleaseModel();
            Close();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text.Trim();
            _foundIndices.Clear();
            _foundCurrent = -1;

            if (string.IsNullOrEmpty(query))
            {
                SearchCounter.Text = "";
                return;
            }

            for (int i = 0; i < Messages.Count; i++)
            {
                if (Messages[i].Text?.Contains(query,
                    StringComparison.OrdinalIgnoreCase) == true)
                    _foundIndices.Add(i);
            }

            if (_foundIndices.Count == 0)
            {
                SearchCounter.Text = "Не найдено";
                return;
            }

            _foundCurrent = 0;
            RefreshSearchCounter();
            JumpToFound();
        }

        private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выбери любой GGUF файл в папке с моделями",
                
                Filter = "GGUF модели|*.gguf",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true) return;

            _modelsFolder = Path.GetDirectoryName(dialog.FileName) ?? "";
            ScanForModels(_modelsFolder);

            // 1. Фильтруем список (убираем mmproj)
            var visibleModels = ModelFiles
                .Where(m => !Path.GetFileName(m.FullPath)
                    .Contains("mmproj", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // 2. Привязываем отфильтрованный список к ComboBox
            ModelComboBox.ItemsSource = visibleModels;

            // 3. Выбираем первый доступный элемент автоматически
            if (visibleModels.Any())
            {
                SelectedModel = visibleModels.First();
            }
        }

        private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true; // предотвращаем вставку новой строки
                await SendUserMessageAsync();
            }
        }
   
        private void SearchNext_Click(object sender, RoutedEventArgs e)
        {
            if (_foundIndices.Count == 0) return;
            _foundCurrent = (_foundCurrent + 1) % _foundIndices.Count;
            RefreshSearchCounter();
            JumpToFound();
        }

        private void SearchPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_foundIndices.Count == 0) return;
            _foundCurrent = (_foundCurrent - 1 + _foundIndices.Count)
                            % _foundIndices.Count;
            RefreshSearchCounter();
            JumpToFound();
        }

        private void BtnMode_Click(object sender, RoutedEventArgs e)
        {
            var modeWin = new ModeWindow
            {
                Owner = this,
                CurrentModeName = ActiveModeName
            };

            if (modeWin.ShowDialog() != true) return;

            ActiveSystemPrompt = modeWin.ResultPrompt ?? ActiveSystemPrompt;
            ActiveModeName = modeWin.ResultModeName ?? ActiveModeName;

            if (_llamaWeights != null)
            {
                // Очищаем ожидающее изображение при смене режима
                if (_pendingImagePath != null)
                {
                    _pendingImagePath = null;
                    ShowNotification("⚠️ Прикреплённое изображение отменено при смене режима", 2);
                }

                // Уничтожаем старый контекст (он содержит всю историю токенов)
                _chatSession = null;
                _llamaContext?.Dispose();
                _llamaContext = null;

                // Создаём свежий контекст из тех же весов — веса не перегружаются!
                // Это быстро, занимает доли секунды
                var mp = new ModelParams(SelectedModel!.FullPath)
                {
                    ContextSize = 16384,
                    FlashAttention = true,
                    GpuLayerCount = 32,
                    BatchSize = 512,
                };

                _llamaContext = _llamaWeights.CreateContext(mp);

                // Теперь контекст чистый — AddAndProcessSystemMessage не упадёт
                // Важно: если есть mmproj, создаём executor с мультимодальностью!
                var executor = _mtmdWeights != null
                    ? new InteractiveExecutor(_llamaContext, _mtmdWeights)
                    : new InteractiveExecutor(_llamaContext);

                _chatSession = new ChatSession(executor);

                _chatSession.OutputTransform = new LLamaTransforms.KeywordTextOutputStreamTransform(
                    keywords: new[] { "<think>", "</think>" },
                    redundancyLength: 8
                );

                _chatSession.AddAndProcessSystemMessage(ActiveSystemPrompt);

                _modelReady = true;
                ShowNotification($"🎭 {ActiveModeName} — системный промпт применён", 3);
            }
            else
            {
                ShowNotification($"🎭 Режим: {ActiveModeName} (модель не загружена)", 3);
            }
        }

        #endregion


        #region Область методов программные   

        public void ShowNotification(string text, int seconds = 4)
        {
            Dispatcher.Invoke(() =>
            {
                var border = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(220, 30, 30, 30)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 6, 0, 0),
                    Opacity = 0,
                    Child = new TextBlock
                    {
                        Text = text,
                        Foreground = System.Windows.Media.Brushes.White,
                        TextWrapping = TextWrapping.Wrap
                    }
                };

                NotificationsPanel.Children.Insert(0, border);

                border.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));

                var timer = new System.Timers.Timer(seconds * 1000);
                timer.Elapsed += (s, ev) =>
                {
                    timer.Stop();
                    Dispatcher.Invoke(() =>
                    {
                        var fadeOut = new DoubleAnimation(1, 0,
                            TimeSpan.FromMilliseconds(300));
                        fadeOut.Completed += (s2, e2) =>
                            NotificationsPanel.Children.Remove(border);
                        border.BeginAnimation(OpacityProperty, fadeOut);
                    });
                };
                timer.Start();
            });
        }

        // Начать ресайз через Win32
        private void BeginResize(IntPtr edge)
        {
            try
            {
                ReleaseCapture();
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SendMessage(hwnd, WM_NCLBUTTONDOWN, edge, IntPtr.Zero);
            }
            catch (Exception)
            {
                // можно логировать ошибку, если нужно
            }
        }

        private async Task LoadModelAsync(string modelPath)
        {
            SetLoadingState(true, "Загрузка модели...");

            try
            {
                // Анимируем прогресс до 90% пока идёт загрузка
                var progressTask = AnimateProgressBar(0, 90, durationMs: 3500);

                await Task.Run(() =>
                {
                    var mp = new ModelParams(modelPath)
                    {
                        ContextSize = 16384,
                        FlashAttention = true,
                        GpuLayerCount = 32,     // 0 = CPU. Для GPU поставь 35+                                            
                        BatchSize = 512,
                    };

                    _llamaWeights = LLamaWeights.LoadFromFile(mp);
                    
                    _llamaContext = _llamaWeights.CreateContext(mp);

                    // Если есть mmproj — загружаем его тоже
                    if (SelectedModel?.MmprojPath != null)
                    {
                        _mtmdWeights = MtmdWeights.LoadFromFile(
                            SelectedModel.MmprojPath,  // путь к mmproj файлу
                            _llamaWeights!,             // уже загруженные веса текстовой модели
                            new MtmdContextParams()    // параметры контекста (можно оставить по умолчанию)
                        );
                    }

                });

                // Завершаем прогресс до 100%
                await AnimateProgressBar(90, 100, durationMs: 300);

                // Создаём сессию чата
                var executor = _mtmdWeights != null
                ? new InteractiveExecutor(_llamaContext!, _mtmdWeights)
                : new InteractiveExecutor(_llamaContext!);
                _chatSession = new ChatSession(executor);
                _chatSession.OutputTransform = new LLamaTransforms.KeywordTextOutputStreamTransform(
                                                keywords: new[] { "<think>", "</think>" },
                                                redundancyLength: 8
                                                );
                await _chatSession.AddAndProcessSystemMessage(ActiveSystemPrompt);
                _modelReady = true;

                ShowNotification(
                    $"✅ Готово: {Path.GetFileNameWithoutExtension(modelPath)}", 4);
            }
            catch (Exception ex)
            {
                _modelReady = false;
                ShowNotification($"❌ Ошибка загрузки:\n{ex.Message}", 7);
            }
            finally
            {
                SetLoadingState(false, "");
            }
        }

        // Освобождаем память от модели
        private void ReleaseModel()
        {
            _modelReady = false;
            _chatSession = null;

            _llamaContext?.Dispose();
            _llamaContext = null;

            _llamaWeights?.Dispose();
            _llamaWeights = null;

            // Выгружаем mmproj
            _mtmdWeights?.Dispose();
            _mtmdWeights = null;

            _pendingImagePath = null;
        }


        // Плавное заполнение прогрессбара
        private async Task AnimateProgressBar(double from, double to, int durationMs)
        {
            int steps = 40;
            int delay = durationMs / steps;
            double step = (to - from) / steps;

            for (int i = 0; i <= steps; i++)
            {
                double val = from + step * i;
                Dispatcher.Invoke(() => ModelLoadProgress.Value = val);
                await Task.Delay(delay);
            }
        }

        // Показать/скрыть UI состояния загрузки
        private void SetLoadingState(bool loading, string statusText)
        {
            Dispatcher.Invoke(() =>
            {
                var vis = loading ? Visibility.Visible : Visibility.Collapsed;

                LoadingBar.Visibility = vis;
                LoadingStatusText.Visibility = vis;
                LoadingStatusText.Text = statusText;

                if (!loading)
                    ModelLoadProgress.Value = 0;

                // Блокируем ввод пока грузится
                InputBox.IsEnabled = !loading;
                ModelComboBox.IsEnabled = !loading;
            });
        }

        private async Task SendUserMessageAsync()
        {
            var userText = InputBox?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(userText) && _pendingImagePath == null) return;

            if (!_modelReady || _chatSession == null)
            {
                ShowNotification("⚠️ Выбери модель и дождись загрузки", 3);
                return;
            }

            if (_generating) return;

            string messageToSend;
            string displayText;

            if (_pendingImagePath != null && _mtmdWeights != null)
            {
                var imagePath = _pendingImagePath;
                var filename = Path.GetFileName(imagePath);
                _pendingImagePath = null;
                var mediaMarker = NativeApi.MtmdDefaultMarker() ?? "<__media__>";
                _mtmdWeights.ClearMedia();
                // Обход проблемы с кириллицей/пробелами в пути для native API
                // Сжимаем изображение перед загрузкой
                byte[]? imageBytes;
                try
                {
                    imageBytes = await CompressImageAsync(imagePath, maxWidth: 512, jpegQuality: 85);
                    if (imageBytes == null || imageBytes.Length == 0)
                    {
                        ShowNotification($"❌ Не удалось обработать изображение", 5);
                        return;
                    }

                    // Логируем размер для отладки
                    ShowNotification($"🖼 Подготовлено: {(imageBytes.Length / 1024f):F1} KB", 2);
                }
                catch (Exception ex)
                {
                    ShowNotification($"❌ Ошибка обработки:\n{ex.Message}", 5);
                    return;
                }
                SafeMtmdEmbed embed;
                try
                {
                    embed = _mtmdWeights.LoadMedia(imageBytes)
                        ?? throw new RuntimeError($"Failed to load media '{imagePath}'.");
                }
                catch (RuntimeError ex)
                {
                    ShowNotification($"❌ {ex.Message}", 5);
                    return;
                }
                // Embeds executor'а НЕ трогаем — маркер уже в тексте,
                // LoadMedia положил embed в очередь MtmdWeights для Tokenize
                messageToSend = string.IsNullOrWhiteSpace(userText)
                    ? mediaMarker
                    : $"{mediaMarker}\n{userText}";
                displayText = string.IsNullOrWhiteSpace(userText)
                    ? $"🖼 {filename}"
                    : $"🖼 {filename}\n{userText}";
            }
            else
            {
                messageToSend = userText;
                displayText = userText;
            }

            Messages.Add(new ChatMessage { Text = displayText, IsUser = true });
            InputBox?.Clear();
            ScrollChatToBottom();

            var botBubble = new ChatMessage { Text = "", IsUser = false };
            Messages.Add(botBubble);
            ScrollChatToBottom();

            bool hadImage = messageToSend.Contains(
            NativeApi.MtmdDefaultMarker() ?? "<__media__>",
            StringComparison.Ordinal);
            if (hadImage)
            {
                Dispatcher.Invoke(() => botBubble.Text = "⏳ Обрабатываю изображение...");
                ScrollChatToBottom();
            }

            _generating = true;
            _generationCts = new CancellationTokenSource();

            try
            {
                var buffer = new StringBuilder();

                await foreach (var token in _chatSession.ChatAsync(
                    new ChatHistory.Message(AuthorRole.User, messageToSend),
                    BuildInferenceParams(),
                    _generationCts.Token))
                {
                    buffer.Append(token);
                    var snapshot = buffer.ToString();
                    Dispatcher.Invoke(() => botBubble.Text = snapshot);
                    ScrollChatToBottom();
                }
            }
            catch (OperationCanceledException)
            {
                // Отменено — нормально
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => botBubble.Text = $"[Ошибка: {ex.Message}]");
            }
            finally
            {
                _generating = false;
                _generationCts?.Dispose();
                _generationCts = null;
                Dispatcher.Invoke(() => InputBox?.Focus());
                ScrollChatToBottom();
            }
        }

        private void ScanForModels(string folderPath)
        {
            ModelFiles.Clear();

            if (!Directory.Exists(folderPath))
            {
                ShowNotification("Папка не найдена: " + folderPath);
                return;
            }

            var ggufPaths = Directory.GetFiles(folderPath, "*.gguf",
                                               SearchOption.TopDirectoryOnly);
            if (ggufPaths.Length == 0)
            {
                ShowNotification("GGUF файлы не найдены в папке");
                return;
            }

            foreach (var path in ggufPaths)
            {
                if (Path.GetFileName(path)
                        .Contains("mmproj", StringComparison.OrdinalIgnoreCase))
                    continue;

                var entry = new GgufModel { FullPath = path };
                var folder = Path.GetDirectoryName(path) ?? "";

                var mmproj = Directory
                    .GetFiles(folder, "*mmproj*.gguf", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();

                if (mmproj != null)
                    entry.MmprojPath = mmproj;

                ModelFiles.Add(entry);
            }

            SelectedModel = ModelFiles.FirstOrDefault();
            ShowNotification($"Найдено моделей: {ModelFiles.Count}");
        }

        private void ToggleSearchPanel()
        {
            bool nowVisible = SearchPanel.Visibility == Visibility.Visible;
            SearchPanel.Visibility = nowVisible
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (!nowVisible)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
            else
            {
                SearchBox.Text = "";
                SearchCounter.Text = "";
                _foundIndices.Clear();
                _foundCurrent = -1;
            }
        }

        private void RefreshSearchCounter() => SearchCounter.Text = $"{_foundCurrent + 1} / {_foundIndices.Count}";
    

        private void JumpToFound()
        {
            if (_foundCurrent < 0 || _foundCurrent >= _foundIndices.Count) return;

            int idx = _foundIndices[_foundCurrent];
            if (ChatScroll.Content is not ItemsControl ic) return;

            ic.UpdateLayout();
            var container = ic.ItemContainerGenerator
                              .ContainerFromIndex(idx) as FrameworkElement;
            container?.BringIntoView();
        }

        private void ScrollChatToBottom()
        {
            Dispatcher.Invoke(() =>
            {
                try { ChatScroll?.ScrollToEnd(); }
                catch { }
            });
        }

        private void DoResize(IntPtr edge)
        {
            try
            {
                ReleaseCapture();
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SendMessage(hwnd, WM_NCLBUTTONDOWN, edge, IntPtr.Zero);
            }
            catch { }
        }

        private InferenceParams BuildInferenceParams() => new InferenceParams
        {
            SamplingPipeline = new DefaultSamplingPipeline()
            {
                Temperature = 0.7f,
                TopK = 40,
                TopP = 0.9f,
                // Главное от зависания — штраф за повторения
                RepeatPenalty = 1.15f,
            },
            MaxTokens = 1024
        };


        private void ToggleMaxRestore()
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        #endregion

        private async void ModelComboBox_SelectionChanged(object sender,
                                                           SelectionChangedEventArgs e)
        {
            if (SelectedModel == null) return;

            // Прерываем текущую генерацию если идёт
            _generationCts?.Cancel();

            // Выгружаем старую модель
            ReleaseModel();

            // Разблокируем скрепку только при наличии mmproj
            BtnAttach.IsEnabled = SelectedModel.MmprojPath != null;

            // Уведомление о выбранной модели
            var info = $"Модель: {Path.GetFileNameWithoutExtension(SelectedModel.FullPath)}";

            info += SelectedModel.MmprojPath != null
                ? "\n🖼 Поддерживается мультимодальный режим"
                : "\n💬 Только текстовый режим";

            ShowNotification(info, 4);

            // Загружаем новую
            await LoadModelAsync(SelectedModel.FullPath);
        }

        private void BtnAttach_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выбери изображение",
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.webp"
            };

            if (dialog.ShowDialog() != true) return;

            _pendingImagePath = dialog.FileName;

            // Показываем превью в поле ввода
            var filename = Path.GetFileName(_pendingImagePath);
            ShowNotification($"🖼 Прикреплено: {filename}", 3);
        }

        private void StopGenerationToken(object sender, RoutedEventArgs e)
        {
            try
            {
                // Проверяем, идёт ли генерация
                if (_generationCts != null && !_generationCts.IsCancellationRequested)
                {
                    _generationCts.Cancel(); // Останавливаем генерацию
                    ShowNotification("⏹ Генерация остановлена", 3);
                }
                else
                {
                    ShowNotification("⚠️ Генерация уже завершена", 3);
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка при остановке: {ex.Message}", 3);
            }
        }

            //maxWidth: 512, jpegQuality: 80 — максимум производительность(может потеряться деталь)
            //maxWidth: 768, jpegQuality: 85 — баланс(рекомендую стартовать с этого)
            //maxWidth: 1024, jpegQuality: 90 — максимум качество(но медленнее)
        private async Task<byte[]?> CompressImageAsync(string imagePath,
    int maxWidth = 768, int maxHeight = 768, int jpegQuality = 85)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var originalImage = System.Drawing.Image.FromFile(imagePath))
                    {
                        double scale = Math.Min(
                            (double)maxWidth / originalImage.Width,
                            (double)maxHeight / originalImage.Height
                        );

                        if (scale >= 1.0)
                        {
                            return SaveAsJpeg(originalImage, jpegQuality);
                        }

                        int newWidth = (int)(originalImage.Width * scale);
                        int newHeight = (int)(originalImage.Height * scale);

                        using (var resized = new System.Drawing.Bitmap(newWidth, newHeight))
                        {
                            resized.SetResolution(originalImage.HorizontalResolution,
                                                 originalImage.VerticalResolution);

                            using (var g = System.Drawing.Graphics.FromImage(resized))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                                g.DrawImage(originalImage, 0, 0, newWidth, newHeight);
                            }

                            return SaveAsJpeg(resized, jpegQuality);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ShowNotification($"❌ Ошибка обработки изображения:\n{ex.Message}", 5);
                    return null;  // ← Теперь OK (nullable return type)
                }
            });
        }

        private byte[] SaveAsJpeg(System.Drawing.Image image, int quality)
        {
            using (var ms = new MemoryStream())
            {
                var encoder = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                    .First(c => c.MimeType == "image/jpeg");

                var encParams = new System.Drawing.Imaging.EncoderParameters(1);
                encParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, (long)quality);

                image.Save(ms, encoder, encParams);
                return ms.ToArray();
            }
        }



    }


}
