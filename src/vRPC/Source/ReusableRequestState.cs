namespace DanilovSoft.vRPC
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;

    [DebuggerDisplay("{State}")]
    internal struct ReusableRequestState
    {
        /// <summary>
        /// Решает какой поток будет выполнять Reset.
        /// <list type="bullet">
        /// <item>0 - сброшен</item>
        /// <item>1 - готов к отправке</item>
        /// <item>2 - в процессе отправки</item>
        /// <item>3 - отправлен</item>
        /// <item>4 - получен ответ</item>
        /// <item>5 - завершен с ошибкой</item>
        /// </list>
        /// </summary>
        private int _state;
        public ReusableRequestStateEnum State => (ReusableRequestStateEnum)_state;


        // 0.
        /// <summary>
        /// Безусловно устанавливает статус <see cref="ReusableRequestStateEnum.Reset"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 0.</remarks>
        /// <returns>Прошлый статус.</returns>
        public void Reset()
        {
            Debug.Assert(State
                is ReusableRequestStateEnum.GotResponse
                or ReusableRequestStateEnum.GotErrorResponse);

            Interlocked.Exchange(ref _state, 0);
        }

        // 1.
        /// <summary>
        /// Безусловно устанавливает статус <see cref="ReusableRequestStateEnum.ReadyToSend"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 1.</remarks>
        /// <returns>Прошлый статус.</returns>
        public void Ready()
        {
            Debug.Assert(State == ReusableRequestStateEnum.Reset);

            Interlocked.Exchange(ref _state, 1);
        }

        // 2.
        /// <summary>Переводит в состояние <see cref="ReusableRequestStateEnum.Sending"/> 
        /// если состояние <see cref="ReusableRequestStateEnum.ReadyToSend"/>
        /// </summary>
        /// <remarks>Переводит в состояние 2.</remarks>
        public ReusableRequestStateEnum TrySetSending()
        {
            Debug.Assert(State == ReusableRequestStateEnum.ReadyToSend);

            // в состояние 'в процессе отправки' можно перевести только из состояния 'готов к отправке'.
            int state = Interlocked.CompareExchange(ref _state, 2, 1);
            return (ReusableRequestStateEnum)state;
        }

        // 3.
        /// <summary>
        /// Переводит в состояние <see cref="ReusableRequestStateEnum.Sended"/>
        /// если состояние <see cref="ReusableRequestStateEnum.Sending"/>
        /// </summary>
        /// <remarks>Переводит в состояние 3.</remarks>
        public ReusableRequestStateEnum TrySetSended()
        {
            Debug.Assert(State == ReusableRequestStateEnum.Sending);

            // установить это состояние можно только из состояния 'в процессе отправки'.
            int state = Interlocked.CompareExchange(ref _state, 3, 2);
            return (ReusableRequestStateEnum)state;
        }

        // 4.
        /// <summary>
        /// Безусловно устанавливает статус <see cref="ReusableRequestStateEnum.GotResponse"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 4.</remarks>
        /// <returns>Прошлый статус.</returns>
        public ReusableRequestStateEnum SetGotResponse()
        {
            Debug.Assert(State
                is ReusableRequestStateEnum.Sending
                or ReusableRequestStateEnum.Sended
                or ReusableRequestStateEnum.GotErrorResponse);

            Interlocked.Exchange(ref _state, 4);
            return (ReusableRequestStateEnum)_state;
        }

        // 5.
        /// <summary>
        /// Безусловно устанавливает статус <see cref="ReusableRequestStateEnum.GotErrorResponse"/>.
        /// </summary>
        /// <remarks>Переводит в состояние 5.</remarks>
        /// <returns>Прошлый статус.</returns>
        public ReusableRequestStateEnum SetErrorResponse()
        {
            Debug.Assert(State
                is ReusableRequestStateEnum.ReadyToSend
                or ReusableRequestStateEnum.Sending
                or ReusableRequestStateEnum.Sended
                or ReusableRequestStateEnum.GotErrorResponse);

            // Установка этого статуса выполняется безусловно.
            int state = Interlocked.Exchange(ref _state, 5);
            return (ReusableRequestStateEnum)state;
        }

        //public static implicit operator ReusableRequestStateEnum(ReusableRequestState state) => state.State;
    }

    internal enum ReusableRequestStateEnum
    {
        Reset = 0,
        ReadyToSend = 1,
        Sending = 2,
        Sended = 3,
        GotResponse = 4,
        GotErrorResponse = 5,
    }
}
