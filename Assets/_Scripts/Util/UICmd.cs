using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniRx;

public static class Cmd
{
    public interface ICmd<T> {
        Task<T> Execute();
    }
    public sealed class CmdNone<T> : ICmd<T> {
        public Task<T> Execute() {
            return Task.FromResult<T>(default);
        }
    }
    public sealed class CmdAsync<T> : ICmd<T> {
        readonly Func<Task<T>> asyncFunc;
        public CmdAsync(Func<Task<T>> asyncFunc) {
            this.asyncFunc = asyncFunc;
        }

        public async Task<T> Execute() {
            var result = await this.asyncFunc();
            return result;
        }
    }

    public sealed class CmdFunc<T> : ICmd<T>
    {
        readonly Func<T> func;

        public CmdFunc(Func<T> func) {
            this.func = func;
        }

        public Task<T> Execute() {
            return Task.FromResult(this.func());
        }
    }

    public static ICmd<T> OfAsync<T>(Func<Task<T>> asyncF) {
        return new CmdAsync<T>(asyncF);
    }

    public static ICmd<T> OfFunc<T>(Func<T> f) {
        return new CmdFunc<T>(f);
    }

    public static ICmd<T> None<T>() {
        return new CmdNone<T>();
    }

    public static IObservable<ModelT> CreateRenderLoop<IntentT, ModelT>(IObservable<IntentT>  intents, (ModelT, ICmd<IntentT>) initial, Func<ModelT, IntentT, (ModelT, ICmd<IntentT>)> reduce) {
        var cmdSubject = new Subject<IntentT>();

        return
            intents
                .Merge(cmdSubject.ObserveOn(Scheduler.MainThread))
                .Scan(initial,
                    (arg, intent) => {
                        Task.Run(
                            () => {
                                var task = arg.Item2.Execute();
                                task.Wait();
                                if (task.Result != null)
                                    cmdSubject.OnNext(task.Result);
                            });
                        return reduce(arg.Item1, intent);
                    })
                .Select((arg) => arg.Item1);
    }
}