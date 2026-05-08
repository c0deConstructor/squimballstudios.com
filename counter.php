<?php
header('Content-Type: application/json');
header('Access-Control-Allow-Origin: *');

$dataDir  = __DIR__ . '/data';
$dataFile = $dataDir . '/vc.json';

// Prefer real IP; handle Cloudflare / reverse-proxy headers
function clientIP(): string {
    foreach (['HTTP_CF_CONNECTING_IP', 'HTTP_X_FORWARDED_FOR', 'HTTP_X_REAL_IP', 'REMOTE_ADDR'] as $h) {
        if (!empty($_SERVER[$h])) {
            $ip = trim(explode(',', $_SERVER[$h])[0]);
            if (filter_var($ip, FILTER_VALIDATE_IP)) return $ip;
        }
    }
    return '0.0.0.0';
}

// Create data dir + block direct HTTP access on first run
if (!is_dir($dataDir)) {
    mkdir($dataDir, 0755, true);
    file_put_contents($dataDir . '/.htaccess', "Deny from all\n");
}

$fp = fopen($dataFile, 'c+');
if (!$fp) { echo json_encode(['n' => 0]); exit; }

flock($fp, LOCK_EX);

$raw = stream_get_contents($fp);
$db  = ($raw !== '') ? json_decode($raw, true) : null;
if (!is_array($db)) $db = ['seq' => 0, 'v' => []];

// Store SHA-256 hash of IP — never the raw address
$key = hash('sha256', clientIP());
if (!isset($db['v'][$key])) {
    $db['v'][$key] = ++$db['seq'];
}
$n = $db['v'][$key];

rewind($fp);
ftruncate($fp, 0);
fwrite($fp, json_encode($db));
flock($fp, LOCK_UN);
fclose($fp);

echo json_encode(['n' => $n]);
