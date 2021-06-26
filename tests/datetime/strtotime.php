<?php
namespace date\strtotime;

function test() {
    echo date("Y-m-d H:i", strtotime("now")) . PHP_EOL;
    echo date("Y-m-d H:i", strtotime("10 September 2000")) . PHP_EOL;
    echo date("Y-m-d H:i", strtotime("+1 day")) . PHP_EOL;
    echo date("Y-m-d H:i", strtotime("-1 week")) . PHP_EOL;
    echo date("Y-m-d H:i", strtotime("+1 week 2 days 4 hours 2 seconds")) . PHP_EOL;
    echo date("Y-m-d H:i", strtotime("next Thursday")) . PHP_EOL;
    echo date("Y-m-d H:i", strtotime("last Monday")) . PHP_EOL;
    echo date("Y-m-d H:i", strtotime("2020-07-14")) . PHP_EOL;
    echo date("Y-m-d H:i", strtotime("2020-01")) . PHP_EOL;
    echo date("Y-m-d H:i", strtotime("first monday of 2020-01")) . PHP_EOL;
}

test();
