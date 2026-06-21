#region Область using
using LLama;
using LLama.Common;
using Microsoft.Extensions.AI;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using static System.Net.WebRequestMethods;
#endregion
namespace JustOneClick
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Свойства и поля
        // Список сообщений чата — привязан к ItemsControl в XAML
        // Список сообщений чата — привязан к ItemsControl в XAML
        public ObservableCollection<ChatMessage> Messages { get; }
            = new ObservableCollection<ChatMessage>();

        // INotifyPropertyChanged — уведомляем UI об изменении свойств
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // Список найденных GGUF файлов — привязан к ComboBox
        public ObservableCollection<GgufModel> ModelFiles { get; }
            = new ObservableCollection<GgufModel>();

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
            = "Ты — универсальный помощник. Отвечай чётко, по делу.";

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
        private List<int> _foundIndices = new();  // индексы сообщений
        private int _foundCurrent = -1;     // текущий выбранный

        #endregion

        #region Область для перетаскивания и ресайза

        // P/Invoke для ресайза
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private readonly IntPtr HT_LEFT = new IntPtr(10);
        private readonly IntPtr HT_RIGHT = new IntPtr(11);
        private readonly IntPtr HT_TOP = new IntPtr(12);
        private readonly IntPtr HT_BOTTOM = new IntPtr(15);
        private readonly IntPtr HT_TOPLEFT = new IntPtr(13);
        private readonly IntPtr HT_TOPRIGHT = new IntPtr(14);
        private readonly IntPtr HT_BOTTOMLEFT = new IntPtr(16);
        private readonly IntPtr HT_BOTTOMRIGHT = new IntPtr(17);

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

        #region Область объявление класов и переменных 

        public class ModelFile
        {
            // Полный путь к .gguf файлу
            public string FullPath { get; set; } = "";

            // Полный путь к mmproj файлу (null если не найден)
            public string? MmprojPath { get; set; }

            // То что показывается в ComboBox
            public string DisplayName =>
                System.IO.Path.GetFileNameWithoutExtension(FullPath)
                + (MmprojPath != null ? "👁" : "");
            // значок глаза если есть mmproj — визуальная подсказка
        }

        private TextBox? GetInputBox()
        {
            return InputBox;
        }
        // Класс сообщения вынесен наружу (в том же namespace)
        public class Message
        {
            public string? Text { get; set; }
            public bool IsUser { get; set; } // true = пользователь (право), false = бот (лево)
            public DateTime Time { get; set; } = DateTime.Now;
        }

        private List<int> _searchResults = new();  // индексы найденных сообщений

        private int _searchIndex = -1;             // текущий результат

        #endregion

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
                DragMove(); // встроенный метод WPF
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

            // Скрываем mmproj файлы из ComboBox — они вспомогательные
            ModelComboBox.ItemsSource = ModelFiles
                .Where(m => !Path.GetFileName(m.FullPath)
                    .Contains("mmproj", StringComparison.OrdinalIgnoreCase))
                .ToList();
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

            // Сбрасываем сессию — новый промпт применится с первого сообщения
            if (_chatSession != null && _llamaContext != null)
            {
                _chatSession = new ChatSession(
                    new InteractiveExecutor(_llamaContext));
                ShowNotification($"🎭 {ActiveModeName} — сессия сброшена", 3);
            }
            else
            {
                ShowNotification($"🎭 Режим: {ActiveModeName}", 3);
            }
        }

        #endregion


        #region Область методов программные   

        // Обновить счётчик "2 / 5"
        private void UpdateSearchCounter()
        {
            SearchCounter.Text = $"{_searchIndex + 1} / {_searchResults.Count}";
        }

        // Сбросить подсветку (задел на будущее)
        private void ClearSearchHighlights()
        {
            // Здесь можно будет убирать цветовую подсветку
            // когда добавим выделение найденного текста
        }

        // Прокрутить к найденному сообщению
        private void ScrollToSearchResult()
        {
            if (_searchIndex < 0 || _searchIndex >= _searchResults.Count) return;

            int msgIndex = _searchResults[_searchIndex];

            // Просим ItemsControl показать элемент
            var container = GetMessageContainer(msgIndex);
            container?.BringIntoView();
        }

        // Получить визуальный контейнер сообщения по индексу
        private FrameworkElement? GetMessageContainer(int index)
        {
            // ItemsControl должен сгенерировать контейнер
            var itemsControl = ChatScroll.Content as ItemsControl;
            if (itemsControl == null) return null;

            itemsControl.UpdateLayout();
            return itemsControl.ItemContainerGenerator
                               .ContainerFromIndex(index) as FrameworkElement;
        }

        // Сканирование папки — ищем все .gguf файлы
        private void ScanModelsFolder(string folderPath)
        {
            if (folderPath == null) throw new ArgumentNullException(nameof(folderPath));
            ModelFiles.Clear();

            if (!Directory.Exists(folderPath))
            {
                ShowNotification("Папка не найдена: " + folderPath);
                return;
            }

            // Ищем все .gguf файлы в папке (не рекурсивно)
            // тут можно поменять SearchOption чтобы искать не только в текущей папке, а и в подпапках
            var ggufFiles = Directory.GetFiles(folderPath, "*.gguf",
                                               SearchOption.TopDirectoryOnly);

            if (ggufFiles.Length == 0)
            {
                ShowNotification("GGUF файлы не найдены в папке");
                return;
            }

            foreach (var ggufPath in ggufFiles)
            {
                var model = new GgufModel { FullPath = ggufPath };

                var folder = Path.GetDirectoryName(ggufPath) ?? "";

                // Ищем любой файл, содержащий "mmproj" в имени
                var mmproj = Directory.GetFiles(folder, "*mmproj*.gguf", SearchOption.TopDirectoryOnly).FirstOrDefault();

                if (mmproj != null)
                    model.MmprojPath = mmproj;

                ModelFiles.Add(model);
            }

            // Выбираем первую модель автоматически
            SelectedModel = ModelFiles.FirstOrDefault();

            ShowNotification($"Найдено моделей: {ModelFiles.Count}");
        }

        private void ScrollToBottom()
        {
            try
            {
                ChatScroll?.ScrollToEnd();
            }
            catch
            {
                // безопасно игнорируем, если элемент ещё не готов
            }
        }

        // Общая логика отправки сообщения и имитации ответа бота


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
                    Opacity = 0
                };

                border.Child = new TextBlock
                {
                    Text = text,
                    Foreground = System.Windows.Media.Brushes.White,
                    TextWrapping = TextWrapping.Wrap
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
                        ContextSize = 4096,
                        GpuLayerCount = 0,     // 0 = CPU. Для GPU поставь 35+
                        Threads = Math.Max(1, Environment.ProcessorCount - 1),
                                            
                        BatchSize = 512,
                    };

                    _llamaWeights = LLamaWeights.LoadFromFile(mp);
                    _llamaContext = _llamaWeights.CreateContext(mp);
                });

                // Завершаем прогресс до 100%
                await AnimateProgressBar(90, 100, durationMs: 300);

                // Создаём сессию чата
                var executor = new InteractiveExecutor(_llamaContext!);
                _chatSession = new ChatSession(executor);
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
            var userText = InputBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(userText)) return;

            // Модель должна быть загружена
            if (!_modelReady || _chatSession == null)
            {
                ShowNotification("⚠️ Выбери модель и дождись загрузки", 3);
                return;
            }

            // Не отправляем пока идёт генерация
            if (_generating) return;

            // Добавляем сообщение пользователя в чат
            Messages.Add(new ChatMessage { Text = userText, IsUser = true });
            InputBox!.Clear();
            ScrollChatToBottom();

            // Создаём пустой пузырь бота — будем заполнять по токенам
            var botBubble = new ChatMessage { Text = "", IsUser = false };
            Messages.Add(botBubble);
            ScrollChatToBottom();

            _generating = true;
            _generationCts = new CancellationTokenSource();

            try
            {
                var buffer = new StringBuilder();

                var inferParams = new InferenceParams
                {
                    MaxTokens = 1024,

                    // Стоп-токены — модель замолкает при их появлении
                    AntiPrompts = new[]
                    {
                        "<|user|>"
                    }
                };

                // Первое сообщение в сессии — добавляем системный промпт
                bool isFirstMessage = !_chatSession.History.Messages
                    .Any(m => m.AuthorRole == AuthorRole.User);

                var messageText = isFirstMessage
                    ? $"{ActiveSystemPrompt}\n\n{userText}"
                    : userText;

                // Потоковая генерация — токены приходят один за другим
                await foreach (var token in _chatSession.ChatAsync(
                    new ChatHistory.Message(AuthorRole.User, messageText),
                    inferParams,
                    _generationCts.Token))
                {
                    buffer.Append(token);

                    // Обновляем текст пузыря в реальном времени
                    // ChatMessage реализует INotifyPropertyChanged —
                    // UI сам перерисует TextBlock без Remove/Insert
                    var snapshot = buffer.ToString();
                    Dispatcher.Invoke(() => botBubble.Text = snapshot);
                    ScrollChatToBottom();
                }
            }
            catch (OperationCanceledException)
            {
                // Генерация отменена — нормально, не показываем ошибку
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                    botBubble.Text = $"[Ошибка: {ex.Message}]");
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
                var entry = new GgufModel { FullPath = path };
                var folder = Path.GetDirectoryName(path) ?? "";

                // Ищем mmproj рядом
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
            var ic = ChatScroll.Content as ItemsControl;
            if (ic == null) return;

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
            var info = $"Модель: {SelectedModel.DisplayName}";
            info += SelectedModel.MmprojPath != null
                ? $"\nMMProj: {Path.GetFileName(SelectedModel.MmprojPath)}"
                : "\nТолько текстовый режим";
            ShowNotification(info, 4);

            // Загружаем новую
            await LoadModelAsync(SelectedModel.FullPath);
        }

    }

}
