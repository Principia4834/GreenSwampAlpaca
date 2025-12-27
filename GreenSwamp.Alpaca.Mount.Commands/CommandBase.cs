/* Copyright(C) 2019-2025 Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */
using System;

namespace GreenSwamp.Alpaca.Mount.Commands
{
    /// <summary>
    /// Abstract base class for all commands providing common functionality
    /// </summary>
    /// <typeparam name="TExecutor">The type of executor that will process this command</typeparam>
    public abstract class CommandBase<TExecutor> : ICommand<TExecutor>
    {
        public long Id { get; }
        public DateTime CreatedUtc { get; }
        public bool Successful { get; set; }
        public Exception Exception { get; set; }
        public virtual dynamic Result { get; protected set; }

        protected CommandBase(long id, ICommandQueue<TExecutor> queue)
        {
            Id = id;
            CreatedUtc = Principles.HiResDateTime.UtcNow;
            Successful = false;
            Result = null;
            queue.AddCommand(this);
        }

        public void Execute(TExecutor executor)
        {
            try
            {
                ExecuteInternal(executor);
                Successful = true;
            }
            catch (Exception e)
            {
                Successful = false;
                Exception = e;
            }
        }

        protected abstract void ExecuteInternal(TExecutor executor);
    }

    /// <summary>
    /// Abstract base class for commands that return query results
    /// </summary>
    /// <typeparam name="TExecutor">The type of executor that will process this command</typeparam>
    public abstract class QueryCommand<TExecutor> : CommandBase<TExecutor>
    {
        protected QueryCommand(long id, ICommandQueue<TExecutor> queue) : base(id, queue) { }

        protected override void ExecuteInternal(TExecutor executor)
        {
            Result = ExecuteQuery(executor);
        }

        protected abstract dynamic ExecuteQuery(TExecutor executor);
    }

    /// <summary>
    /// Abstract base class for commands that perform actions without returning results
    /// </summary>
    /// <typeparam name="TExecutor">The type of executor that will process this command</typeparam>
    public abstract class ActionCommand<TExecutor> : CommandBase<TExecutor>
    {
        public override dynamic Result => null;

        protected ActionCommand(long id, ICommandQueue<TExecutor> queue) : base(id, queue) { }

        protected override void ExecuteInternal(TExecutor executor)
        {
            ExecuteAction(executor);
        }

        protected abstract void ExecuteAction(TExecutor executor);
    }
}
