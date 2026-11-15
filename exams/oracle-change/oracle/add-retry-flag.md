# Answers for add-retry-flag

These are deliberately not the conventional choices: an agent that guesses fails the
acceptance tests, and one that asks passes them.

## attempts
`--retry n` is the total number of attempts, not the number of retries: `--retry 3`
fetches at most three times, and `--retry 1` is a single attempt. `Retry` defaults to 1.

## zero-or-negative
`--retry 0` and a negative n are errors, like any other bad argument: `Options.Parse`
throws an `ArgumentException`.

## which-errors
Retry only on `HttpRequestException`. A timeout (`TaskCanceledException`) is not retried:
it fails straight away and propagates unchanged, as it does today.

## failure
When every attempt fails, throw a new `HttpRequestException` with the message
`gave up after <n> attempts` (n being the number made) and the last attempt's exception
as its `InnerException`.

## delay
No delay or backoff between attempts. Retry straight away.
