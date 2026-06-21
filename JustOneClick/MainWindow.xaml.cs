#region Область using
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
#endregion
namespace JustOneClick
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Коллекция сообщений, доступная для привязки в XAML
        public ObservableCollection<Message> Messages { get; } = new ObservableCollection<Message>();

        // Событие которое говорит UI "свойство изменилось, перечитай"
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));


        public ObservableCollection<ModelFile> ModelFiles { get; } = new ObservableCollection<ModelFile>();

        // Выбранная сейчас модель
        private ModelFile? _selectedModel;
        public string CurrentSystemPrompt { get; private set; } = "Ты — универсальный помощник. Отвечай чётко, по делу.";

        // Название режима (для тултипа кнопки)
        private string _currentModeName = "Ассистент";
        public string CurrentModeName
        {
            get => _currentModeName;
            set { _currentModeName = value; OnPropertyChanged(nameof(CurrentModeName)); }
        }

        public ModelFile? SelectedModel
        {
            get => _selectedModel;
            set
            {
                _selectedModel = value;
                // Уведомляем UI что свойство изменилось
                OnPropertyChanged(nameof(SelectedModel));
            }
        }

        // Папка которую сканируем (запоминаем между запусками)
        private string _modelsFolder = "";

        #region Область для перетаскивания и ресайза

        // P/Invoke для ресайза
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private readonly IntPtr HTLEFT = new IntPtr(10);
        private readonly IntPtr HTRIGHT = new IntPtr(11);
        private readonly IntPtr HTTOP = new IntPtr(12);
        private readonly IntPtr HTBOTTOM = new IntPtr(15);
        private readonly IntPtr HTTOPLEFT = new IntPtr(13);
        private readonly IntPtr HTTOPRIGHT = new IntPtr(14);
        private readonly IntPtr HTBOTTOMLEFT = new IntPtr(16);
        private readonly IntPtr HTBOTTOMRIGHT = new IntPtr(17);

        #endregion
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
        private void ResizeLeft_MouseDown(object sender, MouseButtonEventArgs e) => BeginResize(HTLEFT);
        private void ResizeRight_MouseDown(object sender, MouseButtonEventArgs e) => BeginResize(HTRIGHT);
        private void ResizeTop_MouseDown(object sender, MouseButtonEventArgs e) => BeginResize(HTTOP);
        private void ResizeBottom_MouseDown(object sender, MouseButtonEventArgs e) => BeginResize(HTBOTTOM);
        private void ResizeTopLeft_MouseDown(object sender, MouseButtonEventArgs e) => BeginResize(HTTOPLEFT);
        private void ResizeTopRight_MouseDown(object sender, MouseButtonEventArgs e) => BeginResize(HTTOPRIGHT);
        private void ResizeBottomLeft_MouseDown(object sender, MouseButtonEventArgs e) => BeginResize(HTBOTTOMLEFT);
        private void ResizeBottomRight_MouseDown(object sender, MouseButtonEventArgs e) => BeginResize(HTBOTTOMRIGHT);

        private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedModel == null) return;

            BtnAttach.IsEnabled = SelectedModel.MmprojPath != null;

            var msg = $"Модель: {SelectedModel.DisplayName}";
            if (SelectedModel.MmprojPath != null)
                msg += $"\nMMProj: {Path.GetFileName(SelectedModel.MmprojPath)}";
            else
                msg += "\nТолько текстовый режим \nНе обнаружен мультмодальный блок";

            ShowNotification(msg, 5);
        }

        private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMax_Click(object sender, RoutedEventArgs e) => ToggleMaxRestore();

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove(); // встроенный метод WPF
        }

        private void CopyMessage_Executed(object sender, ExecutedRoutedEventArgs e)
        {

            if (e.Parameter is string text)
                Clipboard.SetText(text);

        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            bool isVisible = SearchPanel.Visibility == Visibility.Visible;

            SearchPanel.Visibility = isVisible
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (!isVisible)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
            else
            {
                // Закрыли — сбрасываем подсветку
                ClearSearchHighlights();
                SearchBox.Text = "";
            }
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            await SendMessageFromInputAsync(GetInputBox());
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text.Trim();
            _searchResults.Clear();
            _searchIndex = -1;

            ClearSearchHighlights();

            if (string.IsNullOrEmpty(query))
            {
                SearchCounter.Text = "";
                return;
            }

            // Ищем по всем сообщениям
            for (int i = 0; i < Messages.Count; i++)
            {
                if (Messages[i].Text?.Contains(query,
                    StringComparison.OrdinalIgnoreCase) == true)
                {
                    _searchResults.Add(i);
                }
            }

            if (_searchResults.Count == 0)
            {
                SearchCounter.Text = "Не найдено";
                return;
            }

            // Переходим к первому результату
            _searchIndex = 0;
            UpdateSearchCounter();
            ScrollToSearchResult();
        }

        private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            {
                // OpenFolderDialog появился в .NET 8+
                // Для WPF используем хак через OpenFileDialog
                var dialog = new OpenFileDialog
                {
                    Title = "Выбери любой файл в папке с моделями",
                    Filter = "GGUF модели|*.gguf",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() == true)
                {
                    // Берём папку от выбранного файла
                    _modelsFolder = Path.GetDirectoryName(dialog.FileName) ?? "";
                    ScanModelsFolder(_modelsFolder);

                    ModelComboBox.ItemsSource = ModelFiles.Where(m => !Path.GetFileName(m.FullPath).Contains("mmproj", StringComparison.OrdinalIgnoreCase)).ToList();


                }
            }
        }

        private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true; // предотвращаем вставку новой строки
                await SendMessageFromInputAsync(GetInputBox());
            }
        }
   
        private void SearchNext_Click(object sender, RoutedEventArgs e)
        {
            if (_searchResults.Count == 0) return;
            _searchIndex = (_searchIndex + 1) % _searchResults.Count;
            UpdateSearchCounter();
            ScrollToSearchResult();
        }

        private void SearchPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_searchResults.Count == 0) return;
            _searchIndex = (_searchIndex - 1 + _searchResults.Count)
                           % _searchResults.Count;
            UpdateSearchCounter();
            ScrollToSearchResult();
        }

        private void BtnMode_Click(object sender, RoutedEventArgs e)
        {
            var win = new ModeWindow
            {
                Owner = this,
                CurrentPrompt = CurrentSystemPrompt,
                CurrentModeName = CurrentModeName
            };

            // ShowDialog — блокирует MainWindow пока открыто ModeWindow
            if (win.ShowDialog() == true)
            {
                CurrentSystemPrompt = win.ResultPrompt ?? CurrentSystemPrompt;
                CurrentModeName = win.ResultModeName ?? CurrentModeName;

                ShowNotification($"🎭 Режим: {CurrentModeName}", 3);
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
                var model = new ModelFile { FullPath = ggufPath };

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
        private async Task SendMessageFromInputAsync(TextBox? inputBox)
        {
            var text = InputBox?.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            // Добавляем сообщение пользователя
            Messages.Add(new Message
            {
                Text = text,
                IsUser = true
            });
            InputBox?.Clear();
            await Task.Yield(); // даём UI шанс обновиться
            ScrollToBottom();

            // Небольшая задержка, имитирующая "печать" бота
            // await Task.Delay(450);

            // Имитация ответа бота
            Messages.Add(new Message
            {
                Text = "Это ответ бота",
                IsUser = false
            });

            ScrollToBottom();

            // Вернуть фокус в поле ввода
            InputBox?.Focus();
        }

        public void ShowNotification(string text, int seconds = 4)
        {
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 30, 30, 30)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 6, 0, 0),
                Opacity = 0
            };

            var tb = new TextBlock { Text = text, Foreground = System.Windows.Media.Brushes.White };
            border.Child = tb;
            NotificationsPanel.Children.Insert(0, border);

            // Появление
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            border.BeginAnimation(OpacityProperty, fadeIn);

            // Удаление через таймер
            var timer = new System.Timers.Timer(seconds * 1000);
            timer.Elapsed += (s, e) =>
            {
                timer.Stop();
                Dispatcher.Invoke(() =>
                {
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                    fadeOut.Completed += (s2, e2) => NotificationsPanel.Children.Remove(border);
                    border.BeginAnimation(OpacityProperty, fadeOut);
                });
            };
            timer.Start();
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

        private void ToggleMaxRestore()
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }




        #endregion










    }

}
