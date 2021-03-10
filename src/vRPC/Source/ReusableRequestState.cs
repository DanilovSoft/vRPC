namespace DanilovSoft.vRPC
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;

    /// <summary>
    /// <list type="bullet">
    /// <item>1 - сброшен</item>
    /// <item>2 - готов к отправке</item>
    /// <item>3 - в процессе отправки</item>
    /// <item>4 - отправлен</item>
    /// <item>5 - получен ответ</item>
    /// <item>6 - завершен с ошибкой</item>
    /// </list>
    /// </summary>
    [DebuggerDisplay("{State}")]
    internal struct ReusableRequestState
    {
        /// <summary>
        /// Решает какой поток будет выполнять Reset.
        /// <list type="bullet">
        /// <item>1 - сброшен</item>
        /// <item>2 - готов к отправке</item>
        /// <item>3 - в процессе отправки</item>
        /// <item>4 - отправлен</item>
        /// <item>5 - получен ответ</item>
        /// <item>6 - завершен с ошибкой</item>
        /// </list>
        /// </summary>
        private int _state;
        public ReusableRequestStateEnum State => (ReusableRequestStateEnum)_state;

        public ReusableRequestState(ReusableRequestStateEnum state)
        {
            _state = (int)state;
        }

        // 1.
        /// <summary>
        /// Безусловно устанавливает статус <see cref="ReusableRequestStateEnum.Reset"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 1.</remarks>
        /// <returns>Прошлый статус.</returns>
        public void Reset()
        {
            Debug.Assert(State
                is ReusableRequestStateEnum.GotResponse
                or ReusableRequestStateEnum.GotErrorResponse);

            Interlocked.Exchange(ref _state, 1);
        }

        // 2.
        /// <summary>
        /// Безусловно устанавливает статус <see cref="ReusableRequestStateEnum.ReadyToSend"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 2.</remarks>
        /// <returns>Прошлый статус.</returns>
        public void SetReady()
        {
            Debug.Assert(State == ReusableRequestStateEnum.Reset);

            Interlocked.Exchange(ref _state, 2);
        }

        // 3.
        /// <summary>Переводит в состояние <see cref="ReusableRequestStateEnum.Sending"/> 
        /// если состояние <see cref="ReusableRequestStateEnum.ReadyToSend"/>
        /// </summary>
        /// <remarks>Переводит в состояние 3.</remarks>
        public ReusableRequestStateEnum TrySetSending()
        {
            Debug.Assert(State == ReusableRequestStateEnum.ReadyToSend);

            // в состояние 'в процессе отправки' можно перевести только из состояния 'готов к отправке'.
            int state = Interlocked.CompareExchange(ref _state, 3, 2);
            return (ReusableRequestStateEnum)state;
        }

        // 4.
        /// <summary>
        /// Переводит в состояние <see cref="ReusableRequestStateEnum.Sended"/>
        /// если состояние <see cref="ReusableRequestStateEnum.Sending"/>
        /// </summary>
        /// <remarks>Переводит в состояние 4.</remarks>
        public ReusableRequestStateEnum TrySetSended()
        {
            // Во время отправки уже мог прийти ответ и поменять статус или могла произойти ошибка.
            Debug.Assert(State 
                is ReusableRequestStateEnum.Sending
                or ReusableRequestStateEnum.GotResponse 
                or ReusableRequestStateEnum.GotErrorResponse);

            // установить это состояние можно только из состояния 'в процессе отправки'.
            int state = Interlocked.CompareExchange(ref _state, 4, 3);
            return (ReusableRequestStateEnum)state;
        }

        // 5.
        /// <summary>
        /// Безусловно устанавливает статус <see cref="ReusableRequestStateEnum.GotResponse"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 5.</remarks>
        /// <returns>Прошлый статус.</returns>
        public ReusableRequestStateEnum SetGotResponse()
        {
            Debug.Assert(State
                is ReusableRequestStateEnum.Sending
                or ReusableRequestStateEnum.Sended
                or ReusableRequestStateEnum.GotErrorResponse);

            Interlocked.Exchange(ref _state, 5);
            return (ReusableRequestStateEnum)_state;
        }

        // 6.
        /// <summary>
        /// Безусловно устанавливает статус <see cref="ReusableRequestStateEnum.GotErrorResponse"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 6.</remarks>
        /// <returns>Прошлый статус.</returns>
        public ReusableRequestStateEnum SetErrorResponse()
        {
            Debug.Assert(State
                is ReusableRequestStateEnum.ReadyToSend
                or ReusableRequestStateEnum.Sending
                or ReusableRequestStateEnum.Sended
                or ReusableRequestStateEnum.GotErrorResponse);

            // Установка этого статуса выполняется безусловно.
            int state = Interlocked.Exchange(ref _state, 6);
            return (ReusableRequestStateEnum)state;
        }
    }

    internal enum ReusableRequestStateEnum
    {
        None = 0,
        Reset = 1,
        ReadyToSend = 2,
        Sending = 3,
        Sended = 4,
        GotResponse = 5,
        GotErrorResponse = 6,
    }
}
