<?php
// imap_utf7_encode, imap_utf7_decode

// Arrange
$str = '~peter&/mail/日本語/台北';
//$str_utf7modified = '~peter/mail/&ZeVnLIqe-/&U,BTFw-';
$str_utf7modified = '&ZeVnLIqe-';

// Act
$str_utf7_test = imap_utf7_encode($str);
$str_utf7_test2 = imap_utf7_decode($str_utf7modified);

//Assert
echo $str_utf7_test ."\n";
print_r(unpack('C*', $str_utf7_test2 ));