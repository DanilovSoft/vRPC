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
    internal struct OldReusableRequestState
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
        public ReusableRequestState State => (ReusableRequestState)_state;

        public OldReusableRequestState(ReusableRequestState state)
        {
            _state = (int)state;
        }

        // 1.
        /// <summary>
        /// Безусловно устанавливает статус <see cref="ReusableRequestState.Reset"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 1.</remarks>
        /// <returns>Прошлый статус.</returns>
        public void Reset()
        {
            Debug.Assert(State
                is ReusableRequestState.GotResponse
                or ReusableRequestState.GotErrorResponse);

            Interlocked.Exchange(ref _state, 1);
        }

        // 2.
        /// <summary>
        /// Безусловно устанавливает статус <see cref="ReusableRequestState.ReadyToSend"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 2.</remarks>
        /// <returns>Прошлый статус.</returns>
        public void SetReady()
        {
            Debug.Assert(State == ReusableRequestState.Reset);

            Interlocked.Exchange(ref _state, 2);
        }

        // 3.
        /// <summary>
        /// Переводит в состояние <see cref="ReusableRequestState.Sending"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 3.</remarks>
        public ReusableRequestState SetSending()
        {
            Debug.Assert(State == ReusableRequestState.ReadyToSend);

            // в состояние 'в процессе отправки' можно перевести только из состояния 'готов к отправке'.
            int state = Interlocked.Exchange(ref _state, 3);
            return (ReusableRequestState)state;
        }

        // 4.
        /// <summary>
        /// Переводит в состояние <see cref="ReusableRequestState.WaitingResponse"/>
        /// </summary>
        /// <remarks>Переводит в состояние 4.</remarks>
        public ReusableRequestState SetWaitingResponse()
        {
            // Во время отправки уже мог прийти ответ и поменять статус или могла произойти ошибка.
            Debug.Assert(State 
                is ReusableRequestState.Sending
                or ReusableRequestState.GotResponse 
                or ReusableRequestState.GotErrorResponse);

            // установить это состояние можно только из состояния 'в процессе отправки'.
            int state = Interlocked.Exchange(ref _state, 4);
            return (ReusableRequestState)state;
        }

        // 5.
        /// <summary>
        /// Безусловно устанавливает статус <see cref="ReusableRequestState.GotResponse"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 5.</remarks>
        /// <returns>Прошлый статус.</returns>
        public ReusableRequestState SetGotResponse()
        {
            Debug.Assert(State
                is ReusableRequestState.Sending
                or ReusableRequestState.WaitingResponse
                or ReusableRequestState.GotErrorResponse);

            Interlocked.Exchange(ref _state, 5);
            return (ReusableRequestState)_state;
        }

        // 6.
        /// <summary>
        /// Безусловно устанавливает статус <see cref="ReusableRequestState.GotErrorResponse"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 6.</remarks>
        /// <returns>Прошлый статус.</returns>
        public ReusableRequestState SetErrorResponse()
        {
            Debug.Assert(State
                is ReusableRequestState.ReadyToSend
                or ReusableRequestState.Sending
                or ReusableRequestState.WaitingResponse
                or ReusableRequestState.GotErrorResponse);

            // Установка этого статуса выполняется безусловно.
            int state = Interlocked.Exchange(ref _state, 6);
            return (ReusableRequestState)state;
        }
    }
}
