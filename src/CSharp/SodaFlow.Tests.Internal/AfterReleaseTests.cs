using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Covers AfterReleaseImpl: an action runs on the same thread after the
///     transaction that holds the lock releases it.
/// </summary>
public sealed class AfterReleaseTests
{
    [Test]
    public async Task AnActionRunsAfterThePostsWithTheLockFree()
    {
        List<string> order = [];
        bool lockWasFree = false;

        TransactionInternal.Apply((_, _) =>
        {
            TransactionInternal.AfterReleaseImpl(() =>
            {
                order.Add("after release");
                lockWasFree = LockIsFree();
            });

            TransactionInternal.PostImpl(() => order.Add("post"));
            order.Add("body");
            return UnitInternal.Value;
        });

        await Assert.That(order)
            .IsEquivalentTo(expected: ["body", "post", "after release"], ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    // A posted action runs in a child transaction, thus an action that it asks for waits. It waits
    // also for the posts after it, which include a post that the posted action makes.
    [Test]
    public async Task AnActionFromAPostRunsAfterAllThePostsWithTheLockFree()
    {
        List<string> order = [];
        bool lockWasFree = false;

        TransactionInternal.Apply((_, _) =>
        {
            TransactionInternal.PostImpl(() =>
            {
                TransactionInternal.AfterReleaseImpl(() =>
                {
                    order.Add("after release");
                    lockWasFree = LockIsFree();
                });

                TransactionInternal.PostImpl(() => order.Add("post from the post"));
                order.Add("post");
            });

            order.Add("body");
            return UnitInternal.Value;
        });

        await Assert.That(order)
            .IsEquivalentTo(
                expected: ["body", "post", "post from the post", "after release"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    // A split also runs in a child transaction, thus an action that it asks for waits. It waits also
    // for a post that the split makes, which the drain runs after the round of splits.
    [Test]
    public async Task AnActionFromASplitRunsAfterAllThePostsWithTheLockFree()
    {
        List<string> order = [];
        bool lockWasFree = false;

        TransactionInternal.Apply((trans, _) =>
        {
            trans.Split(
                index: 0,
                action: _ =>
                {
                    TransactionInternal.AfterReleaseImpl(() =>
                    {
                        order.Add("after release");
                        lockWasFree = LockIsFree();
                    });

                    TransactionInternal.PostImpl(() => order.Add("post from the split"));
                    order.Add("split");
                });

            order.Add("body");
            return UnitInternal.Value;
        });

        await Assert.That(order)
            .IsEquivalentTo(
                expected: ["body", "split", "post from the split", "after release"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    // A post from a post runs in the next generation of posts, and its split runs in a round after
    // that generation. An action from that split waits for all of these, and for the post from the
    // split after them.
    [Test]
    public async Task AnActionFromASplitOfANestedPostRunsAfterAllThePostsWithTheLockFree()
    {
        List<string> order = [];
        bool lockWasFree = false;

        TransactionInternal.Apply((_, _) =>
        {
            TransactionInternal.PostImpl(() =>
            {
                TransactionInternal.PostImpl(() =>
                {
                    TransactionInternal.CurrentTransaction()
                        .Split(
                            index: 0,
                            action: _ =>
                            {
                                TransactionInternal.AfterReleaseImpl(() =>
                                {
                                    order.Add("after release");
                                    lockWasFree = LockIsFree();
                                });

                                TransactionInternal.PostImpl(() => order.Add("post from the split"));
                                order.Add("split");
                            });

                    order.Add("nested post");
                });

                order.Add("post");
            });

            TransactionInternal.PostImpl(() => order.Add("next post"));
            order.Add("body");
            return UnitInternal.Value;
        });

        await Assert.That(order)
            .IsEquivalentTo(
                expected: ["body", "post", "next post", "nested post", "split", "post from the split", "after release"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    // A split that fails drops the posts that it made, but not an action that it asks for. The
    // splits of a different position run in a different child, thus the failure does not stop them.
    [Test]
    public async Task AnActionFromASplitThatFailsRunsAfterAllTheSplitsWithTheLockFree()
    {
        List<string> order = [];
        bool lockWasFree = false;

        await Assert.That(() =>
                TransactionInternal.Apply((trans, _) =>
                {
                    trans.Split(
                        index: 0,
                        action: _ =>
                        {
                            TransactionInternal.AfterReleaseImpl(() =>
                            {
                                order.Add("after release");
                                lockWasFree = LockIsFree();
                            });

                            TransactionInternal.PostImpl(() => order.Add("post from the failed split"));
                            order.Add("split");
                            throw new InvalidOperationException("split");
                        });

                    trans.Split(index: 1, action: _ => order.Add("next split"));
                    return UnitInternal.Value;
                }))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage("split");

        await Assert.That(order)
            .IsEquivalentTo(expected: ["split", "next split", "after release"], ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    // A posted action that fails drops the posts that it made, but not an action that it asks for
    // here. The code that asked for the action ran, as in AnActionRunsAfterATransactionThatFails.
    [Test]
    public async Task AnActionFromAPostThatFailsRunsAfterAllThePostsWithTheLockFree()
    {
        List<string> order = [];
        bool lockWasFree = false;

        await Assert.That(() =>
                TransactionInternal.Apply((_, _) =>
                {
                    TransactionInternal.PostImpl(() =>
                    {
                        TransactionInternal.AfterReleaseImpl(() =>
                        {
                            order.Add("after release");
                            lockWasFree = LockIsFree();
                        });

                        TransactionInternal.PostImpl(() => order.Add("post from the failed post"));
                        order.Add("post");
                        throw new InvalidOperationException("post");
                    });

                    TransactionInternal.PostImpl(() => order.Add("next post"));
                    return UnitInternal.Value;
                }))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage("post");

        await Assert.That(order)
            .IsEquivalentTo(expected: ["post", "next post", "after release"], ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    // The action runs with no transaction open, thus a post from it runs now, in a transaction of
    // its own. The post that fails drops only the posts that it made itself.
    [Test]
    public async Task APostFromTheActionOfAFailedPostRunsAtOnceInATransactionOfItsOwn()
    {
        List<string> order = [];
        bool actionHadATransaction = true;
        bool postHadATransaction = false;

        await Assert.That(() =>
                TransactionInternal.Apply((_, _) =>
                {
                    TransactionInternal.PostImpl(() =>
                    {
                        TransactionInternal.AfterReleaseImpl(() =>
                        {
                            order.Add("after release");
                            actionHadATransaction = TransactionInternal.HasCurrentTransaction();

                            TransactionInternal.PostImpl(() =>
                            {
                                order.Add("post from the after release");
                                postHadATransaction = TransactionInternal.HasCurrentTransaction();
                            });

                            order.Add("after release ends");
                        });

                        order.Add("post");
                        throw new InvalidOperationException("post");
                    });

                    return UnitInternal.Value;
                }))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage("post");

        await Assert.That(order)
            .IsEquivalentTo(
                expected: ["post", "after release", "post from the after release", "after release ends"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(actionHadATransaction).IsFalse();
        await Assert.That(postHadATransaction).IsTrue();
    }

    // The action runs after the drain, thus its throw does not fail the post that asked for it. The
    // post from that post runs only because the post committed.
    [Test]
    public async Task AThrowFromAnActionOfAPostComesAfterTheDrainAndTheActionsAfterItRun()
    {
        List<string> order = [];

        await Assert.That(() =>
                TransactionInternal.Apply((_, _) =>
                {
                    TransactionInternal.PostImpl(() =>
                    {
                        TransactionInternal.AfterReleaseImpl(() =>
                        {
                            order.Add("after release that throws");
                            throw new ArgumentException("action");
                        });

                        TransactionInternal.AfterReleaseImpl(() => order.Add("next after release"));
                        TransactionInternal.PostImpl(() => order.Add("post from the post"));
                        order.Add("post");
                    });

                    order.Add("body");
                    return UnitInternal.Value;
                }))
            .ThrowsExactly<ArgumentException>()
            .WithMessage("action");

        await Assert.That(order)
            .IsEquivalentTo(
                expected: ["body", "post", "post from the post", "after release that throws", "next after release"],
                ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnActionWithNoTransactionOpenRunsAtOnce()
    {
        bool ran = false;
        TransactionInternal.AfterReleaseImpl(() => ran = true);

        await Assert.That(ran).IsTrue();
    }

    // The code that asked for the action ran, thus the action runs also when the transaction fails.
    [Test]
    public async Task AnActionRunsAfterATransactionThatFails()
    {
        bool ran = false;

        await Assert.That(() =>
                TransactionInternal.Apply<UnitInternal>((_, _) =>
                {
                    TransactionInternal.AfterReleaseImpl(() => ran = true);
                    throw new InvalidOperationException("abort");
                }))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage("abort");

        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task AThrowFromAnActionComesAfterTheExceptionOfTheTransaction()
    {
        bool secondRan = false;
        AggregateException? thrown = null;

        try
        {
            TransactionInternal.Apply<UnitInternal>((_, _) =>
            {
                TransactionInternal.AfterReleaseImpl(static () => throw new ArgumentException("action"));
                TransactionInternal.AfterReleaseImpl(() => secondRan = true);
                throw new InvalidOperationException("transaction");
            });
        }
        catch (AggregateException e)
        {
            thrown = e;
        }

        await Assert.That(thrown).IsNotNull();

        // ReSharper disable once NullableWarningSuppressionIsUsed - The assertion above checks it.
        await Assert.That(thrown!.InnerExceptions[0]).IsTypeOf<InvalidOperationException>();
        await Assert.That(thrown.InnerExceptions[1]).IsTypeOf<ArgumentException>();
        await Assert.That(secondRan).IsTrue();
    }

    // A transaction that a start hook opens sees no open transaction, but the transaction of the hook
    // holds the lock. Thus, the action must wait for that transaction.
    [Test]
    public async Task AnActionFromAStartHookWaitsForTheTransactionThatHoldsTheLock()
    {
        int thread = Environment.CurrentManagedThreadId;
        bool armed = true;
        bool lockWasFree = false;
        bool ran = false;

        // A start hook stays for the life of the process, and other tests open transactions on
        // other threads. Thus, the hook acts one time and only on this thread.
        Transaction.OnStart(() =>
        {
            if (!armed || Environment.CurrentManagedThreadId != thread)
            {
                return;
            }

            armed = false;

            Transaction.RunVoid(() =>
                TransactionInternal.AfterReleaseImpl(() =>
                {
                    ran = true;
                    lockWasFree = LockIsFree();
                }));
        });

        Transaction.RunVoid(static () =>
        {
        });

        await Assert.That(ran).IsTrue();
        await Assert.That(lockWasFree).IsTrue();
    }

    // The child transaction of a posted action runs the start hooks. An action that a hook asks for
    // there waits until the drain ends, as an action that the posted action asks for.
    [Test]
    public async Task AnActionFromAStartHookOfAPostRunsAfterAllThePostsWithTheLockFree()
    {
        int thread = Environment.CurrentManagedThreadId;
        List<string> order = [];
        bool postRan = false;
        bool armed = true;
        bool lockWasFree = false;

        // The hook stays until this test ends, and other tests open transactions on other threads.
        // Thus, the hook acts one time, only on this thread, and only after the posted action ran.
        using IDisposable hook = Transaction.OnStart(() =>
        {
            if (!armed || !postRan || Environment.CurrentManagedThreadId != thread)
            {
                return;
            }

            armed = false;

            TransactionInternal.AfterReleaseImpl(() =>
            {
                order.Add("after release");
                lockWasFree = LockIsFree();
            });
        });

        TransactionInternal.Apply((_, _) =>
        {
            TransactionInternal.PostImpl(() =>
            {
                postRan = true;
                order.Add("post");
            });

            TransactionInternal.PostImpl(() => order.Add("next post"));
            return UnitInternal.Value;
        });

        await Assert.That(armed).IsFalse();

        await Assert.That(order)
            .IsEquivalentTo(expected: ["post", "next post", "after release"], ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    // A posted action that fails does not close its child. Thus, the child runs the start hooks only
    // when the action opens a transaction, as a send does. The failure does not drop an action that
    // a hook asks for there.
    [Test]
    public async Task AnActionFromAStartHookOfAPostThatFailsRunsAfterAllThePostsWithTheLockFree()
    {
        int thread = Environment.CurrentManagedThreadId;
        List<string> order = [];
        bool inThePost = false;
        bool armed = true;
        bool lockWasFree = false;

        // The hook stays until this test ends, and other tests open transactions on other threads.
        // Thus, the hook acts one time, only on this thread, and only in the posted action that fails.
        using IDisposable hook = Transaction.OnStart(() =>
        {
            if (!armed || !inThePost || Environment.CurrentManagedThreadId != thread)
            {
                return;
            }

            armed = false;

            TransactionInternal.AfterReleaseImpl(() =>
            {
                order.Add("after release");
                lockWasFree = LockIsFree();
            });
        });

        await Assert.That(() =>
                TransactionInternal.Apply((_, _) =>
                {
                    TransactionInternal.PostImpl(() =>
                    {
                        inThePost = true;

                        Transaction.RunVoid(static () =>
                        {
                        });

                        inThePost = false;
                        order.Add("post");
                        throw new InvalidOperationException("post");
                    });

                    TransactionInternal.PostImpl(() => order.Add("next post"));
                    return UnitInternal.Value;
                }))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage("post");

        await Assert.That(armed).IsFalse();

        await Assert.That(order)
            .IsEquivalentTo(expected: ["post", "next post", "after release"], ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    // The child transaction of a split does not run the start hooks, also where the split opens a
    // transaction. Thus, no action that a hook asks for comes from a split.
    [Test]
    public async Task AStartHookAsksForNoActionInASplit()
    {
        int thread = Environment.CurrentManagedThreadId;
        List<string> order = [];
        bool inTheSplit = false;
        bool hookActed = false;

        // The hook stays until this test ends, and other tests open transactions on other threads.
        // Thus, the hook acts only on this thread, and only in the split.
        using IDisposable hook = Transaction.OnStart(() =>
        {
            if (!inTheSplit || Environment.CurrentManagedThreadId != thread)
            {
                return;
            }

            hookActed = true;
            TransactionInternal.AfterReleaseImpl(() => order.Add("after release from the hook"));
        });

        TransactionInternal.Apply((trans, _) =>
        {
            trans.Split(
                index: 0,
                action: _ =>
                {
                    inTheSplit = true;

                    Transaction.RunVoid(static () =>
                    {
                    });

                    inTheSplit = false;
                    order.Add("split");
                });

            return UnitInternal.Value;
        });

        await Assert.That(hookActed).IsFalse();
        await Assert.That(order).IsEquivalentTo(expected: ["split"], ordering: CollectionOrdering.Matching);
    }

    // A post from a split runs in the child of a post, and that child runs the start hooks. An action
    // that a hook asks for there waits until the drain ends, also for the post from that post.
    [Test]
    public async Task AnActionFromAStartHookOfAPostFromASplitRunsAfterAllThePostsWithTheLockFree()
    {
        int thread = Environment.CurrentManagedThreadId;
        List<string> order = [];
        bool inThePost = false;
        bool armed = true;
        bool lockWasFree = false;

        // The hook stays until this test ends, and other tests open transactions on other threads.
        // Thus, the hook acts one time, only on this thread, and only in the post from the split.
        using IDisposable hook = Transaction.OnStart(() =>
        {
            if (!armed || !inThePost || Environment.CurrentManagedThreadId != thread)
            {
                return;
            }

            armed = false;

            TransactionInternal.AfterReleaseImpl(() =>
            {
                order.Add("after release");
                lockWasFree = LockIsFree();
            });
        });

        TransactionInternal.Apply((trans, _) =>
        {
            trans.Split(
                index: 0,
                action: _ =>
                {
                    TransactionInternal.PostImpl(() =>
                    {
                        inThePost = true;

                        Transaction.RunVoid(static () =>
                        {
                        });

                        inThePost = false;
                        TransactionInternal.PostImpl(() => order.Add("post from that post"));
                        order.Add("post from the split");
                    });

                    order.Add("split");
                });

            return UnitInternal.Value;
        });

        await Assert.That(armed).IsFalse();

        await Assert.That(order)
            .IsEquivalentTo(
                expected: ["split", "post from the split", "post from that post", "after release"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    // A start hook of a post opens a transaction of its own, and a split of that transaction asks for
    // an action. The transaction of the hook does not hold the lock, thus the action waits until the
    // drain of the outer transaction ends.
    [Test]
    public async Task AnActionFromASplitOfAStartHookOfAPostRunsAfterAllThePostsWithTheLockFree()
    {
        int thread = Environment.CurrentManagedThreadId;
        List<string> order = [];
        bool inThePost = false;
        bool armed = true;
        bool lockWasFree = false;

        // The hook stays until this test ends, and other tests open transactions on other threads.
        // Thus, the hook acts one time, only on this thread, and only in the posted action.
        using IDisposable hook = Transaction.OnStart(() =>
        {
            if (!armed || !inThePost || Environment.CurrentManagedThreadId != thread)
            {
                return;
            }

            armed = false;

            TransactionInternal.Apply((trans, _) =>
            {
                trans.Split(
                    index: 0,
                    action: _ =>
                    {
                        TransactionInternal.AfterReleaseImpl(() =>
                        {
                            order.Add("after release");
                            lockWasFree = LockIsFree();
                        });

                        order.Add("split from the hook");
                    });

                return UnitInternal.Value;
            });
        });

        TransactionInternal.Apply((_, _) =>
        {
            TransactionInternal.PostImpl(() =>
            {
                inThePost = true;

                Transaction.RunVoid(static () =>
                {
                });

                inThePost = false;
                TransactionInternal.PostImpl(() => order.Add("post from the post"));
                order.Add("post");
            });

            return UnitInternal.Value;
        });

        await Assert.That(armed).IsFalse();

        await Assert.That(order)
            .IsEquivalentTo(
                expected: ["split from the hook", "post", "post from the post", "after release"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    // A start hook of a post opens a transaction of its own, and a split of that transaction posts.
    // The drain of the transaction of the hook runs that post, and the post asks for an action. The
    // action waits until the drain of the outer transaction ends.
    [Test]
    public async Task AnActionFromAPostOfASplitOfAStartHookOfAPostRunsAfterAllThePostsWithTheLockFree()
    {
        int thread = Environment.CurrentManagedThreadId;
        List<string> order = [];
        bool inThePost = false;
        bool armed = true;
        bool lockWasFree = false;

        // The hook stays until this test ends, and other tests open transactions on other threads.
        // Thus, the hook acts one time, only on this thread, and only in the posted action.
        using IDisposable hook = Transaction.OnStart(() =>
        {
            if (!armed || !inThePost || Environment.CurrentManagedThreadId != thread)
            {
                return;
            }

            armed = false;

            TransactionInternal.Apply((trans, _) =>
            {
                trans.Split(
                    index: 0,
                    action: _ =>
                    {
                        TransactionInternal.PostImpl(() =>
                        {
                            TransactionInternal.AfterReleaseImpl(() =>
                            {
                                order.Add("after release");
                                lockWasFree = LockIsFree();
                            });

                            order.Add("post of the split");
                        });

                        order.Add("split from the hook");
                    });

                return UnitInternal.Value;
            });
        });

        TransactionInternal.Apply((_, _) =>
        {
            TransactionInternal.PostImpl(() =>
            {
                inThePost = true;

                Transaction.RunVoid(static () =>
                {
                });

                inThePost = false;
                TransactionInternal.PostImpl(() => order.Add("post from the post"));
                order.Add("post");
            });

            return UnitInternal.Value;
        });

        await Assert.That(armed).IsFalse();

        await Assert.That(order)
            .IsEquivalentTo(
                expected: ["split from the hook", "post of the split", "post", "post from the post", "after release"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    // A start hook of a post opens a transaction of its own, and that transaction posts. The post
    // splits, and the split asks for an action. The action waits until the drain of the outer
    // transaction ends.
    [Test]
    public async Task AnActionFromASplitOfAPostOfAStartHookOfAPostRunsAfterAllThePostsWithTheLockFree()
    {
        int thread = Environment.CurrentManagedThreadId;
        List<string> order = [];
        bool inThePost = false;
        bool armed = true;
        bool lockWasFree = false;

        // The hook stays until this test ends, and other tests open transactions on other threads.
        // Thus, the hook acts one time, only on this thread, and only in the posted action.
        using IDisposable hook = Transaction.OnStart(() =>
        {
            if (!armed || !inThePost || Environment.CurrentManagedThreadId != thread)
            {
                return;
            }

            armed = false;

            TransactionInternal.Apply((_, _) =>
            {
                TransactionInternal.PostImpl(() =>
                {
                    TransactionInternal.CurrentTransaction()
                        .Split(
                            index: 0,
                            action: _ =>
                            {
                                TransactionInternal.AfterReleaseImpl(() =>
                                {
                                    order.Add("after release");
                                    lockWasFree = LockIsFree();
                                });

                                order.Add("split of the post");
                            });

                    order.Add("post of the hook");
                });

                return UnitInternal.Value;
            });
        });

        TransactionInternal.Apply((_, _) =>
        {
            TransactionInternal.PostImpl(() =>
            {
                inThePost = true;

                Transaction.RunVoid(static () =>
                {
                });

                inThePost = false;
                TransactionInternal.PostImpl(() => order.Add("post from the post"));
                order.Add("post");
            });

            return UnitInternal.Value;
        });

        await Assert.That(armed).IsFalse();

        await Assert.That(order)
            .IsEquivalentTo(
                expected: ["post of the hook", "split of the post", "post", "post from the post", "after release"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    // Opens a transaction on a different thread. Where this thread holds the lock, that transaction
    // waits for it, and the timeout ends the wait.
    private static bool LockIsFree() =>
        Task.Run(static () => Transaction.RunVoid(static () =>
            {
            }))
            .Wait(TimeSpan.FromSeconds(5));
}
