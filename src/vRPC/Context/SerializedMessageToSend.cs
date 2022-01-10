namespace DanilovSoft.vRPC
{
    //[Obsolete]
    //internal class SerializedMessageToSend : IMessageToSend
    //{
    //    public SerializedMessageToSend(ResponseMessage responseToSend)
    //    {
    //        Debug.Assert(false);
    //    }

    //    public SerializedMessageToSend(RequestMethodMeta requestMethodMeta)
    //    {
    //        Debug.Assert(false);
    //    }

    //    public StatusCode? StatusCode { get; internal set; }
    //    public string? ContentEncoding { get; internal set; }
    //    public ArrayBufferWriter<byte> Buffer { get; internal set; }

    //    public ManagedConnection Context 
    //    { 
    //        get { Debug.Assert(false); throw new NotImplementedException(); } 
    //    }

    //    public IMessageMeta MessageToSend { get; internal set; }
    //    public Multipart[]? Parts { get; internal set; }
    //    public int Uid { get; internal set; }
    //    public int HeaderSize { get; internal set; }

    //    internal void Dispose()
    //    {
    //        Debug.Assert(false);
    //        throw new NotImplementedException();
    //    }

    //    internal ValueTask WaitNotificationAsync()
    //    {
    //        Debug.Assert(false);
    //        throw new NotImplementedException();
    //    }
    //}

    //    /// <summary>
    //    /// Является запросом или ответом на запрос.
    //    /// Содержит <see cref="Stream"/> в который сериализуется заголовок
    //    /// и сообщение для отправки удалённой стороне.
    //    /// Необходимо обязательно выполнить Dispose.
    //    /// </summary>
    //    internal sealed partial class SerializedMessageToSend : IMessageToSend, IDisposable
    //    {
    //#if DEBUG
    //        // Что-бы видеть контент в режиме отладки.
    //        private string? DebugJson => GetDebugJson();

    //        internal string? GetDebugJson()
    //        {
    //            if (HeaderSize > 0)
    //            {
    //                if ((ContentEncoding == null || ContentEncoding == "json") && Buffer?.WrittenCount > 0)
    //                {
    //                    byte[] copy = Buffer.WrittenMemory.ToArray();
    //                    string j = Encoding.UTF8.GetString(copy, 0, copy.Length - HeaderSize);
    //                    var element = JsonDocument.Parse(j).RootElement;
    //                    return JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });
    //                }
    //                else
    //                    return null;
    //            }
    //            else
    //                return "Заголовок ещё на записан";
    //        }
    //#endif

    //        [SuppressMessage("Usage", "CA2213:Следует высвобождать высвобождаемые поля", Justification = "Dispose выполняется атомарно")]
    //        private ArrayBufferWriter<byte>? _memPoolBuffer;
    //        /// <summary>
    //        /// Если это сообщение является запросом то содержит сериализованные параметры для удалённого метода или любой 
    //        /// другой тип если это сообщение является ответом на запрос.
    //        /// Заголовок располагается в конце этого стрима, так как мы не можем сформировать заголовок 
    //        /// до сериализации тела сообщения.
    //        /// </summary>
    //        internal ArrayBufferWriter<byte> Buffer
    //        {
    //            get
    //            {
    //                Debug.Assert(_memPoolBuffer != null);
    //                return _memPoolBuffer;
    //            }
    //        }
    //        /// <summary>
    //        /// Запрос или ответ на запрос.
    //        /// Может быть статический объект <see cref="RequestMethodMeta"/> или экземпляр <see cref="ResponseMessage"/>.
    //        /// </summary>
    //        internal IMessageMeta MessageToSend { get; }
    //        /// <summary>
    //        /// Уникальный идентификатор который будет отправлен удалённой стороне.
    //        /// Может быть Null когда не требуется ответ на запрос.
    //        /// </summary>
    //        internal int? Uid { get; set; }
    //        internal StatusCode? StatusCode { get; set; }
    //        internal string? ContentEncoding { get; set; }
    //        /// <summary>
    //        /// Размер хэдера располагающийся в конце стрима.
    //        /// </summary>
    //        internal int HeaderSize { get; set; }
    //        internal Multipart[]? Parts { get; set; }

    //        /// <summary>
    //        /// Содержит <see cref="IBufferWriter{Byte}"/> в который сериализуется сообщение и заголовок.
    //        /// Необходимо обязательно выполнить Dispose.
    //        /// </summary>
    //        internal SerializedMessageToSend(IMessageMeta messageToSend)
    //        {
    //            MessageToSend = messageToSend;

    //            // Арендуем заранее под максимальный размер хэдера.
    //            _memPoolBuffer = new ArrayBufferWriter<byte>(1024);
    //        }

    //        /// <summary>
    //        /// Возвращает арендованную память обратно в пул.
    //        /// </summary>
    //        public void Dispose()
    //        {
    //            Interlocked.Exchange(ref _memPoolBuffer, null)?.Dispose();
    //            if (MessageToSend.IsNotificationRequest)
    //            {
    //                CompleteNotification();
    //            }
    //#if DEBUG
    //            GC.SuppressFinalize(this);
    //#endif
    //        }

    //#if DEBUG
    //        [SuppressMessage("Performance", "CA1821:Удалите пустые завершающие методы", Justification = "Это ловушка для нарушенной логики")]
    //        ~SerializedMessageToSend()
    //        {
    //            Debug.Assert(false, "Деструктор никогда не должен срабатывать");
    //        }
    //#endif
    //    }
}
