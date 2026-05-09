<?php
/**
 * ONE-TIME USE — delete this file from the server after running it.
 * Resets the visitor counter so the next person to visit becomes #1.
 * Access: squimballstudios.com/counter_reset.php?token=squimball_reset
 */
$SECRET = 'squimball_reset';

if (($_GET['token'] ?? '') !== $SECRET) {
    http_response_code(403);
    echo 'Forbidden';
    exit;
}

$dataFile = __DIR__ . '/data/vc.json';

if (file_exists($dataFile)) {
    unlink($dataFile);
    echo 'Counter reset. data/vc.json deleted.<br>Visit the main site now to claim visitor #1.<br><strong>Delete this file from your server.</strong>';
} else {
    echo 'data/vc.json does not exist — counter is already at zero.<br>Visit the main site now to claim visitor #1.<br><strong>Delete this file from your server.</strong>';
}
