import { cn } from '@/lib/utils';
import type { CursorVariant, ResizeDirection } from './types';

type CursorSvgDefinition = {
  width: number;
  height: number;
  viewBox: string;
  markup: string;
};

const cursorSvgDefinitions = {
  arrow: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_333)">
<path d="M15.9231 18.0296C16.0985 18.4505 15.9299 20.0447 15 20.4142C14.0701 20.7837 12.882 20.4142 12.882 20.4142L10.726 16.1024L7 19.8284V3L18.4142 14.4142H14.1615C14.3702 14.8144 15.7003 17.4948 15.9231 18.0296Z" fill="white"/>
<path fill-rule="evenodd" clip-rule="evenodd" d="M8 5.41422V17.4142L11 14.4142L13.5 19.4142C13.5 19.4142 14.1763 19.63 14.5 19.4142C14.8237 19.1984 15.1457 18.7638 15 18.4142C14.3123 16.7638 12.5 13.4142 12.5 13.4142H16L8 5.41422Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_333" x="5.2" y="2.2" width="15.0142" height="21.1784" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.9"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.65 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_333"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_333" result="shape"/>
</filter>
</defs>
`,
  },
  pointer: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_270)">
<path d="M8.27 16.28C7.99 15.92 7.64 15.19 7.03 14.28C6.68 13.78 5.82 12.83 5.56 12.34C5.37257 12.0422 5.31819 11.6797 5.41 11.34C5.56696 10.6942 6.17956 10.2658 6.84 10.34C7.3508 10.4426 7.82022 10.693 8.19 11.06C8.44818 11.3032 8.68567 11.5674 8.9 11.85C9.06 12.05 9.1 12.13 9.28 12.36C9.46 12.59 9.58 12.82 9.49 12.48C9.42 11.98 9.3 11.14 9.13 10.39C9 9.82 8.97 9.73 8.85 9.3C8.73 8.87 8.66 8.51 8.53 8.02C8.41117 7.53858 8.31771 7.05124 8.25 6.56C8.12395 5.93171 8.21566 5.27922 8.51 4.71C8.8594 4.38137 9.37193 4.29464 9.81 4.49C10.2506 4.81534 10.5791 5.26966 10.75 5.79C11.0121 6.43039 11.187 7.10307 11.27 7.79C11.43 8.79 11.74 10.25 11.75 10.55C11.75 10.18 11.68 9.4 11.75 9.05C11.8194 8.68513 12.073 8.38232 12.42 8.25C12.7178 8.15863 13.0328 8.13808 13.34 8.19C13.65 8.25482 13.9247 8.43315 14.11 8.69C14.3417 9.2734 14.4703 9.8926 14.49 10.52C14.5168 9.97059 14.6108 9.42653 14.77 8.9C14.9371 8.66455 15.1811 8.49479 15.46 8.42C15.7906 8.35956 16.1294 8.35956 16.46 8.42C16.7311 8.51063 16.9682 8.68152 17.14 8.91C17.3518 9.44035 17.48 10.0003 17.52 10.57C17.52 10.71 17.59 10.18 17.81 9.83C17.9243 9.4906 18.211 9.23797 18.5621 9.16728C18.9132 9.09659 19.2754 9.21857 19.5121 9.48728C19.7489 9.75599 19.8243 10.1306 19.71 10.47C19.71 11.12 19.71 11.09 19.71 11.53C19.71 11.97 19.71 12.36 19.71 12.73C19.6736 13.3152 19.5933 13.8968 19.47 14.47C19.296 14.9771 19.0538 15.4582 18.75 15.9C18.2645 16.44 17.8633 17.0502 17.56 17.71C17.4848 18.0378 17.4512 18.3738 17.46 18.71C17.459 19.0206 17.4994 19.33 17.58 19.63C17.1711 19.6732 16.7589 19.6732 16.35 19.63C15.96 19.57 15.48 18.79 15.35 18.55C15.2857 18.4211 15.154 18.3397 15.01 18.3397C14.866 18.3397 14.7343 18.4211 14.67 18.55C14.45 18.93 13.96 19.62 13.62 19.66C12.95 19.74 11.57 19.66 10.48 19.66C10.48 19.66 10.66 18.66 10.25 18.3C9.84 17.94 9.42 17.52 9.11 17.24L8.27 16.28Z" fill="white"/>
<path fill-rule="evenodd" clip-rule="evenodd" d="M8.27 16.28C7.99 15.92 7.64 15.19 7.03 14.28C6.68 13.78 5.82 12.83 5.56 12.34C5.37257 12.0422 5.31819 11.6797 5.41 11.34C5.56696 10.6942 6.17956 10.2658 6.84 10.34C7.3508 10.4426 7.82022 10.693 8.19 11.06C8.44818 11.3032 8.68567 11.5674 8.9 11.85C9.06 12.05 9.1 12.13 9.28 12.36C9.46 12.59 9.58 12.82 9.49 12.48C9.42 11.98 9.3 11.14 9.13 10.39C9 9.82 8.97 9.73 8.85 9.3C8.73 8.87 8.66 8.51 8.53 8.02C8.41117 7.53858 8.31771 7.05124 8.25 6.56C8.12395 5.93171 8.21566 5.27922 8.51 4.71C8.85939 4.38137 9.37193 4.29464 9.81 4.49C10.2506 4.81534 10.5791 5.26966 10.75 5.79C11.0121 6.43039 11.187 7.10307 11.27 7.79C11.43 8.79 11.74 10.25 11.75 10.55C11.75 10.18 11.68 9.4 11.75 9.05C11.8194 8.68513 12.073 8.38232 12.42 8.25C12.7178 8.15863 13.0328 8.13808 13.34 8.19C13.65 8.25482 13.9247 8.43315 14.11 8.69C14.3417 9.2734 14.4703 9.8926 14.49 10.52C14.5168 9.97059 14.6108 9.42653 14.77 8.9C14.9371 8.66455 15.1811 8.49479 15.46 8.42C15.7906 8.35956 16.1294 8.35956 16.46 8.42C16.7311 8.51063 16.9682 8.68152 17.14 8.91C17.3518 9.44035 17.48 10.0003 17.52 10.57C17.52 10.71 17.59 10.18 17.81 9.83C17.9243 9.4906 18.211 9.23797 18.5621 9.16728C18.9132 9.09659 19.2754 9.21857 19.5121 9.48728C19.7489 9.75599 19.8243 10.1306 19.71 10.47C19.71 11.12 19.71 11.09 19.71 11.53C19.71 11.97 19.71 12.36 19.71 12.73C19.6736 13.3152 19.5933 13.8968 19.47 14.47C19.296 14.9771 19.0538 15.4582 18.75 15.9C18.2644 16.44 17.8633 17.0502 17.56 17.71C17.4848 18.0378 17.4512 18.3738 17.46 18.71C17.459 19.0206 17.4994 19.33 17.58 19.63C17.1711 19.6732 16.7589 19.6732 16.35 19.63C15.96 19.57 15.48 18.79 15.35 18.55C15.2857 18.4211 15.154 18.3397 15.01 18.3397C14.866 18.3397 14.7343 18.4211 14.67 18.55C14.45 18.93 13.96 19.62 13.62 19.66C12.95 19.74 11.57 19.66 10.48 19.66C10.48 19.66 10.66 18.66 10.25 18.3C9.84 17.94 9.42 17.52 9.11 17.24L8.27 16.28Z" stroke="black" stroke-width="0.75" stroke-linecap="round" stroke-linejoin="round"/>
<path d="M16.75 16.8259V13.3741C16.75 13.1675 16.5821 13 16.375 13C16.1679 13 16 13.1675 16 13.3741V16.8259C16 17.0325 16.1679 17.2 16.375 17.2C16.5821 17.2 16.75 17.0325 16.75 16.8259Z" fill="black"/>
<path d="M14.77 16.8246L14.75 13.3711C14.7488 13.1649 14.5799 12.9988 14.3728 13C14.1657 13.0012 13.9988 13.1693 14 13.3754L14.02 16.8289C14.0212 17.035 14.1901 17.2012 14.3972 17.2C14.6043 17.1988 14.7712 17.0307 14.77 16.8246Z" fill="black"/>
<path d="M12 13.379L12.02 16.8254C12.0212 17.0335 12.1901 17.2012 12.3972 17.2C12.6043 17.1988 12.7712 17.0291 12.77 16.821L12.75 13.3746C12.7488 13.1665 12.5799 12.9988 12.3728 13C12.1657 13.0012 11.9988 13.1709 12 13.379Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_270" x="4.19134" y="4.01178" width="16.7461" height="17.8588" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.4"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.5 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_270"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_270" result="shape"/>
</filter>
</defs>
`,
  },
  grab: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_272)">
<path d="M8.38 12.27C8.28 11.9 8.18 11.42 7.97 10.72C7.76 10.02 7.63 9.86 7.5 9.49C7.37 9.12 7.2 8.77 7 8.31C6.81681 7.84005 6.66317 7.35912 6.54 6.87C6.45676 6.4578 6.55918 6.02986 6.82 5.7C7.17886 5.34982 7.69668 5.21656 8.18 5.35C8.55865 5.51559 8.87907 5.79073 9.1 6.14C9.39504 6.61167 9.63653 7.11478 9.82 7.64C10.1017 8.35963 10.3062 9.10716 10.43 9.87L10.52 10.32C10.52 10.32 10.52 9.2 10.52 9.16C10.52 8.16 10.46 7.34 10.52 6.22C10.52 6.09 10.58 5.63 10.6 5.5C10.6259 5.06996 10.8821 4.68748 11.27 4.5C11.7153 4.30021 12.2247 4.30021 12.67 4.5C13.0686 4.67843 13.3347 5.06401 13.36 5.5C13.36 5.61 13.45 6.5 13.45 6.61C13.45 7.61 13.45 8.25 13.45 8.78C13.45 9.01 13.45 10.41 13.45 10.25C13.4736 8.93046 13.5871 7.61407 13.79 6.31C13.9085 5.90144 14.196 5.56301 14.58 5.38C15.0549 5.19294 15.5945 5.28543 15.98 5.62C16.2682 5.93629 16.4378 6.34269 16.46 6.77C16.46 7.18 16.46 7.67 16.46 8.02C16.46 8.89 16.46 9.34 16.46 10.14C16.46 10.14 16.46 10.44 16.46 10.32C16.55 10.04 16.65 9.78 16.73 9.58C16.81 9.38 16.97 8.97 17.09 8.72C17.2121 8.48166 17.3491 8.25121 17.5 8.03C17.6568 7.77573 17.8919 7.57922 18.17 7.47C18.4205 7.37318 18.6993 7.38115 18.9438 7.49213C19.1884 7.6031 19.378 7.80774 19.47 8.06C19.5295 8.42425 19.5295 8.79575 19.47 9.16C19.4033 9.71943 19.2862 10.2717 19.12 10.81C18.99 11.26 18.85 12.04 18.78 12.41C18.71 12.78 18.55 13.79 18.42 14.23C18.2275 14.7549 17.9615 15.2498 17.63 15.7C17.1448 16.2402 16.7437 16.8504 16.44 17.51C16.3653 17.8379 16.3317 18.1738 16.34 18.51C16.3384 18.8207 16.3788 19.1301 16.46 19.43C16.0512 19.4736 15.6388 19.4736 15.23 19.43C14.84 19.37 14.36 18.59 14.23 18.35C14.1657 18.2211 14.034 18.1397 13.89 18.1397C13.746 18.1397 13.6143 18.2211 13.55 18.35C13.32 18.73 12.84 19.42 12.5 19.46C11.83 19.54 10.45 19.46 9.36 19.46C9.36 19.46 9.55 18.46 9.13 18.1C8.71 17.74 8.3 17.32 7.99 17.04L7.16 16.12C6.88 15.76 6.53 15.03 5.92 14.12C5.57 13.62 4.92 13.03 4.64 12.54C4.40493 12.1423 4.33663 11.6678 4.45 11.22C4.61981 10.6254 5.21067 10.2545 5.82 10.36C6.2835 10.3905 6.72192 10.5815 7.06 10.9C7.32771 11.1316 7.57836 11.3823 7.81 11.65C7.97 11.84 8.01 11.93 8.19 12.16C8.37 12.39 8.49 12.62 8.4 12.28" fill="white"/>
<path d="M8.38 12.27C8.28 11.9 8.18 11.42 7.97 10.72C7.76 10.02 7.63 9.86 7.5 9.49C7.37 9.12 7.2 8.77 7 8.31C6.81681 7.84005 6.66317 7.35912 6.54 6.87C6.45676 6.4578 6.55918 6.02986 6.82 5.7C7.17886 5.34982 7.69668 5.21656 8.18 5.35C8.55865 5.51559 8.87907 5.79073 9.1 6.14C9.39504 6.61167 9.63653 7.11478 9.82 7.64C10.1017 8.35963 10.3062 9.10716 10.43 9.87L10.52 10.32C10.52 10.32 10.52 9.2 10.52 9.16C10.52 8.16 10.46 7.34 10.52 6.22C10.52 6.09 10.58 5.63 10.6 5.5C10.6259 5.06996 10.8821 4.68748 11.27 4.5C11.7153 4.30021 12.2247 4.30021 12.67 4.5C13.0686 4.67843 13.3347 5.06401 13.36 5.5C13.36 5.61 13.45 6.5 13.45 6.61C13.45 7.61 13.45 8.25 13.45 8.78C13.45 9.01 13.45 10.41 13.45 10.25C13.4736 8.93046 13.5871 7.61407 13.79 6.31C13.9085 5.90144 14.196 5.56301 14.58 5.38C15.0549 5.19294 15.5945 5.28543 15.98 5.62C16.2682 5.93629 16.4378 6.34269 16.46 6.77C16.46 7.18 16.46 7.67 16.46 8.02C16.46 8.89 16.46 9.34 16.46 10.14C16.46 10.14 16.46 10.44 16.46 10.32C16.55 10.04 16.65 9.78 16.73 9.58C16.81 9.38 16.97 8.97 17.09 8.72C17.2121 8.48166 17.3491 8.25121 17.5 8.03C17.6568 7.77573 17.8919 7.57922 18.17 7.47C18.4205 7.37318 18.6993 7.38115 18.9438 7.49213C19.1884 7.6031 19.378 7.80774 19.47 8.06C19.5295 8.42425 19.5295 8.79575 19.47 9.16C19.4033 9.71943 19.2862 10.2717 19.12 10.81C18.99 11.26 18.85 12.04 18.78 12.41C18.71 12.78 18.55 13.79 18.42 14.23C18.2275 14.7549 17.9615 15.2498 17.63 15.7C17.1448 16.2402 16.7437 16.8504 16.44 17.51C16.3653 17.8379 16.3317 18.1738 16.34 18.51C16.3384 18.8207 16.3788 19.1301 16.46 19.43C16.0512 19.4736 15.6388 19.4736 15.23 19.43C14.84 19.37 14.36 18.59 14.23 18.35C14.1657 18.2211 14.034 18.1397 13.89 18.1397C13.746 18.1397 13.6143 18.2211 13.55 18.35C13.32 18.73 12.84 19.42 12.5 19.46C11.83 19.54 10.45 19.46 9.36 19.46C9.36 19.46 9.55 18.46 9.13 18.1C8.71 17.74 8.3 17.32 7.99 17.04L7.16 16.12C6.88 15.76 6.53 15.03 5.92 14.12C5.57 13.62 4.92 13.03 4.64 12.54C4.40493 12.1423 4.33663 11.6678 4.45 11.22C4.61981 10.6254 5.21067 10.2545 5.82 10.36C6.2835 10.3905 6.72192 10.5815 7.06 10.9C7.32771 11.1316 7.57836 11.3823 7.81 11.65C7.97 11.84 8.01 11.93 8.19 12.16C8.37 12.39 8.49 12.62 8.4 12.28" stroke="black" stroke-width="0.75" stroke-linecap="round" stroke-linejoin="round"/>
<path d="M15.75 16.4309V12.9791C15.75 12.7725 15.5821 12.605 15.375 12.605C15.1679 12.605 15 12.7725 15 12.9791V16.4309C15 16.6375 15.1679 16.805 15.375 16.805C15.5821 16.805 15.75 16.6375 15.75 16.4309Z" fill="black"/>
<path d="M13.76 16.4307L13.75 12.9771C13.7494 12.771 13.581 12.6044 13.3739 12.605C13.1668 12.6056 12.9994 12.7732 13 12.9793L13.01 16.4328C13.0106 16.639 13.179 16.8056 13.3861 16.805C13.5932 16.8044 13.7606 16.6368 13.76 16.4307Z" fill="black"/>
<path d="M11.005 12.9799L11.025 16.4245C11.0262 16.6331 11.1951 16.8012 11.4022 16.8C11.6093 16.7988 11.7762 16.6287 11.775 16.4201L11.755 12.9755C11.7538 12.7669 11.5849 12.5988 11.3778 12.6C11.1707 12.6012 11.0038 12.7713 11.005 12.9799Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_272" x="3.22148" y="3.97516" width="17.4682" height="17.6954" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.4"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.5 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_272"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_272" result="shape"/>
</filter>
</defs>
`,
  },
  grabbed: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_273)">
<path d="M8 7.15C8.48 6.97 9.43 7.08 9.68 7.62C9.93 8.16 10.08 8.86 10.09 8.69C10.0709 8.17329 10.1145 7.65618 10.22 7.15C10.3312 6.82581 10.5858 6.57115 10.91 6.46C11.2073 6.36597 11.523 6.34538 11.83 6.4C12.1404 6.46389 12.4154 6.64242 12.6 6.9C12.834 7.48313 12.9659 8.10215 12.99 8.73C13.0149 8.19425 13.1056 7.6636 13.26 7.15C13.4271 6.91455 13.6711 6.74479 13.95 6.67C14.2806 6.60955 14.6194 6.60955 14.95 6.67C15.2214 6.76005 15.4587 6.93105 15.63 7.16C15.8424 7.69015 15.9706 8.25024 16.01 8.82C16.01 8.96 16.08 8.43 16.3 8.08C16.4767 7.55533 17.0453 7.27327 17.57 7.45C18.0947 7.62673 18.3767 8.19533 18.2 8.72C18.2 9.37 18.2 9.34 18.2 9.78C18.2 10.22 18.2 10.61 18.2 10.98C18.164 11.5652 18.0837 12.1469 17.96 12.72C17.7864 13.2273 17.5442 13.7084 17.24 14.15C16.7546 14.6901 16.3534 15.3003 16.05 15.96C15.976 16.288 15.9424 16.6238 15.95 16.96C15.949 17.2706 15.9894 17.58 16.07 17.88C15.6612 17.9237 15.2488 17.9237 14.84 17.88C14.45 17.82 13.97 17.04 13.84 16.8C13.7757 16.6711 13.644 16.5897 13.5 16.5897C13.356 16.5897 13.2243 16.6711 13.16 16.8C12.94 17.18 12.45 17.87 12.16 17.91C11.49 17.99 10.1 17.91 9.02 17.91C9.02 17.91 9.21 16.91 8.79 16.55C8.37 16.19 7.96 15.77 7.65 15.49L6.82 14.57C6.23469 14.0267 5.80638 13.3359 5.58 12.57C5.37 11.63 5.39 11.18 5.58 10.8C5.77379 10.4862 6.07639 10.2548 6.43 10.15C6.72377 10.0967 7.02618 10.1173 7.31 10.21C7.50627 10.2922 7.67589 10.4272 7.8 10.6C8.03 10.91 8.11 11.06 8.01 10.72C7.91 10.38 7.69 10.13 7.58 9.72C7.36585 9.23579 7.23728 8.71813 7.2 8.19C7.24098 7.71616 7.57182 7.31756 8.03 7.19" fill="white"/>
<path fill-rule="evenodd" clip-rule="evenodd" d="M8 7.15C8.48 6.97 9.43 7.08 9.68 7.62C9.93 8.16 10.08 8.86 10.09 8.69C10.0709 8.17329 10.1145 7.65618 10.22 7.15C10.3312 6.82581 10.5858 6.57115 10.91 6.46C11.2073 6.36597 11.523 6.34538 11.83 6.4C12.1404 6.46389 12.4154 6.64242 12.6 6.9C12.834 7.48313 12.9659 8.10215 12.99 8.73C13.0149 8.19425 13.1056 7.6636 13.26 7.15C13.4271 6.91455 13.6711 6.74479 13.95 6.67C14.2806 6.60955 14.6194 6.60955 14.95 6.67C15.2214 6.76005 15.4587 6.93105 15.63 7.16C15.8424 7.69015 15.9706 8.25024 16.01 8.82C16.01 8.96 16.08 8.43 16.3 8.08C16.4767 7.55533 17.0453 7.27327 17.57 7.45C18.0947 7.62673 18.3767 8.19533 18.2 8.72C18.2 9.37 18.2 9.34 18.2 9.78C18.2 10.22 18.2 10.61 18.2 10.98C18.164 11.5652 18.0837 12.1469 17.96 12.72C17.7864 13.2273 17.5442 13.7084 17.24 14.15C16.7546 14.6901 16.3534 15.3003 16.05 15.96C15.976 16.288 15.9424 16.6238 15.95 16.96C15.949 17.2706 15.9894 17.58 16.07 17.88C15.6612 17.9237 15.2488 17.9237 14.84 17.88C14.45 17.82 13.97 17.04 13.84 16.8C13.7757 16.6711 13.644 16.5897 13.5 16.5897C13.356 16.5897 13.2243 16.6711 13.16 16.8C12.94 17.18 12.45 17.87 12.16 17.91C11.49 17.99 10.1 17.91 9.02 17.91C9.02 17.91 9.21 16.91 8.79 16.55C8.37 16.19 7.96 15.77 7.65 15.49L6.82 14.57C6.23469 14.0267 5.80638 13.3359 5.58 12.57C5.37 11.63 5.39 11.18 5.58 10.8C5.77379 10.4862 6.07639 10.2548 6.43 10.15C6.72377 10.0967 7.02618 10.1173 7.31 10.21C7.50627 10.2922 7.67589 10.4272 7.8 10.6C8.03 10.91 8.11 11.06 8.01 10.72C7.91 10.38 7.69 10.13 7.58 9.72C7.36585 9.23579 7.23728 8.71813 7.2 8.19C7.22045 7.70923 7.54057 7.29308 8 7.15Z" stroke="black" stroke-width="0.75" stroke-linejoin="round"/>
<path d="M15.75 14.8259V11.3741C15.75 11.1675 15.5821 11 15.375 11C15.1679 11 15 11.1675 15 11.3741V14.8259C15 15.0325 15.1679 15.2 15.375 15.2C15.5821 15.2 15.75 15.0325 15.75 14.8259Z" fill="black"/>
<path d="M13.77 14.8246L13.75 11.3711C13.7488 11.165 13.5799 10.9988 13.3728 11C13.1657 11.0012 12.9988 11.1693 13 11.3754L13.02 14.8289C13.0212 15.035 13.1901 15.2012 13.3972 15.2C13.6043 15.1988 13.7712 15.0307 13.77 14.8246Z" fill="black"/>
<path d="M11 11.3799L11.02 14.8245C11.0212 15.0331 11.1901 15.2012 11.3972 15.2C11.6043 15.1988 11.7712 15.0287 11.77 14.8201L11.75 11.3755C11.7488 11.1669 11.5799 10.9988 11.3728 11C11.1657 11.0012 10.9988 11.1713 11 11.3799Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_273" x="4.2549" y="5.99516" width="15.1729" height="14.1254" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.4"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.5 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_273"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_273" result="shape"/>
</filter>
</defs>
`,
  },
  text: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<path d="M15.8979 5.11121L15.3988 5.07996C15.0502 5.05807 14.7012 5.08054 14.4213 5.12781C14.1554 5.22719 13.9066 5.36039 13.7025 5.50769C13.3818 5.7817 13.1263 6.12277 12.9516 6.50574V11.0214H13.9516V13.0018H12.9516V16.5468C13.1282 16.9295 13.3841 17.2658 13.684 17.5214C13.9134 17.6901 14.1627 17.8251 14.3715 17.9081C14.7121 17.9757 15.0598 18.0023 15.4066 17.9833L15.9057 17.9559L15.933 18.455L15.9877 19.454L16.015 19.953L15.516 19.9803C14.9806 20.0097 14.4449 19.9691 13.8568 19.8417L13.8256 19.8348L13.7953 19.8231C13.3257 19.6555 12.8841 19.4206 12.4623 19.1073L12.4408 19.0897C12.2727 18.9494 12.1151 18.7972 11.9682 18.6366C11.8972 18.7191 11.8242 18.8006 11.7475 18.8798L11.5014 19.118L11.4848 19.1337L11.4672 19.1473C11.1071 19.4217 10.7083 19.637 10.2533 19.7958L10.0541 19.8602L10.0316 19.8671L10.0082 19.8719C9.47804 19.9783 8.9355 20.0144 8.3959 19.9794L7.89786 19.9471L8.02676 17.951L8.52579 17.9843C8.88325 18.0075 9.24068 17.982 9.54434 17.9257C9.7955 17.8449 10.028 17.7255 10.2045 17.5966C10.5271 17.2947 10.784 16.9303 10.9613 16.5262V13.0018H9.96133V11.0214H10.9613V6.53796C10.7836 6.13188 10.5289 5.76935 10.2387 5.495C10.0252 5.33567 9.78988 5.21522 9.58438 5.14636C9.23581 5.0793 8.8801 5.05712 8.52579 5.07996L8.02676 5.11218L7.89786 3.11609L8.39688 3.08386C8.93838 3.04894 9.48118 3.08525 10.06 3.20398L10.0834 3.20886L10.1068 3.21667C10.6003 3.36932 11.0612 3.60723 11.5043 3.94714L11.5219 3.96082L11.5375 3.97546C11.6885 4.11419 11.8311 4.26126 11.9643 4.41589C12.1149 4.25063 12.2775 4.09337 12.4525 3.94617L12.4779 3.92664C12.8802 3.63009 13.3224 3.39546 13.8598 3.20886L13.892 3.19812L13.9262 3.19128C14.4519 3.0865 14.9889 3.05029 15.5238 3.08386L16.0229 3.11511L15.8979 5.11121Z" fill="black" stroke="white"/>
`,
  },
  move: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_278)">
<path d="M12 4L4 12L9.22 17.22L12 20L20 12L12 4ZM10 15H9V14H10V15ZM10 10H9V9H10V10ZM15 15H14V14H15V15ZM14 9H15V10H14V9Z" fill="white"/>
<path d="M18.55 11.98L15.99 9.17V11H11.99H7.98V9.17L5.41 11.98L7.98 14.79V12.98H11.99H15.99V14.79L18.55 11.98Z" fill="black"/>
<path d="M12.97 11.99H12.98V7.99H14.79L11.99 5.42L9.18 7.99H10.99V11.99V15.99H9.17L11.97 18.56L14.78 15.99H12.97V11.99Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_278" x="2.2" y="3.2" width="19.6" height="19.6" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.9"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.65 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_278"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_278" result="shape"/>
</filter>
</defs>
`,
  },
  notAllowed: {
    width: 28,
    height: 40,
    viewBox: '0 0 28 40',
    markup: `
<g filter="url(#filter0_d_2_305)">
<path d="M14 15.5C8.75329 15.5 4.5 19.7533 4.5 25C4.5 30.2467 8.75329 34.5 14 34.5C19.2467 34.5 23.5 30.2467 23.5 25C23.4945 19.7556 19.2444 15.5055 14 15.5ZM10.61 19.5C11.6242 18.8561 12.7987 18.5096 14 18.5C17.5876 18.5055 20.4945 21.4124 20.5 25C20.4904 26.2013 20.1439 27.3758 19.5 28.39L10.61 19.5ZM14 31.5C10.4124 31.4945 7.50551 28.5876 7.5 25C7.50396 23.7885 7.85065 22.6028 8.5 21.58L17.44 30.52C16.4172 31.1694 15.2315 31.516 14.02 31.52L14 31.5Z" fill="white"/>
<path fill-rule="evenodd" clip-rule="evenodd" d="M5 25C5 29.97 9.03 34 14 34C18.97 34 23 29.97 23 25C23 20.029 18.97 16 14 16C9.03 16 5 20.029 5 25ZM9.826 19.39C10.993 18.521 12.434 18 14 18C17.866 18 21 21.134 21 25C21 26.567 20.48 28.008 19.61 29.174L9.826 19.39ZM7 25C7 23.422 7.529 21.971 8.409 20.801L18.199 30.591C17.028 31.472 15.577 32 14 32C10.134 32 7 28.866 7 25Z" fill="url(#paint0_linear_2_305)"/>
<path fill-rule="evenodd" clip-rule="evenodd" d="M4 18.5V2.5L15.6 14.1081H8.55353L8.40242 14.232L4 18.5Z" fill="white"/>
<path fill-rule="evenodd" clip-rule="evenodd" d="M5 4.79999V16L7.969 13.1309L8.129 12.9918L13.165 13L5 4.79999Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_305" x="2.2" y="1.7" width="23.1" height="35.6" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.9"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.65 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_305"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_305" result="shape"/>
</filter>
<linearGradient id="paint0_linear_2_305" x1="14" y1="16" x2="14" y2="34" gradientUnits="userSpaceOnUse">
<stop stop-color="#F1F1F1"/>
<stop offset="1" stop-color="#D6D6D6"/>
</linearGradient>
</defs>
`,
  },
  rotate: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g clip-path="url(#clip0_325_17)">
<g filter="url(#filter0_d_325_17)">
<path d="M11 6C11.9193 6 12.8295 6.18106 13.6788 6.53284C14.5281 6.88463 15.2997 7.40024 15.9497 8.05025C16.5998 8.70026 17.1154 9.47194 17.4672 10.3212C17.8189 11.1705 18 12.0807 18 13V16H22L16 22L10 16H14V13C14 12.606 13.9224 12.2159 13.7716 11.8519C13.6209 11.488 13.3999 11.1573 13.1213 10.8787C12.8427 10.6001 12.512 10.3791 12.1481 10.2284C11.7841 10.0776 11.394 10 11 10H8V14L2 8L8 2V6H11Z" fill="white"/>
<path d="M11 9H7V11.5L3.5 8L7 4.5L7 7H11C11.7879 7 12.5682 7.15519 13.2961 7.45672C14.0241 7.75825 14.6855 8.20021 15.2426 8.75736C15.7998 9.31451 16.2418 9.97594 16.5433 10.7039C16.8448 11.4319 17 12.2121 17 13V17L19.5 17L16 20.5L12.5 17H15V13C15 12.4747 14.8965 11.9546 14.6955 11.4693C14.4945 10.984 14.1999 10.543 13.8284 10.1716C13.457 9.80014 13.016 9.5055 12.5307 9.30448C12.0454 9.10346 11.5253 9 11 9Z" fill="black"/>
</g>
</g>
<defs>
<filter id="filter0_d_325_17" x="0.2" y="1.2" width="23.6" height="23.6" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.9"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.65 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_325_17"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_325_17" result="shape"/>
</filter>
<clipPath id="clip0_325_17">
<rect width="24" height="24" fill="white"/>
</clipPath>
</defs>
`,
  },
};

const resizeSvgDefinitions: Record<ResizeDirection, CursorSvgDefinition> = {
  n: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_296)">
<path d="M18 11L12 5L6 11H10V18H14V11H18Z" fill="white"/>
<path d="M11 17V10H8.41L12 6.41L15.59 10H13V17H11Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_296" x="4.2" y="4.2" width="15.6" height="16.6" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.9"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.65 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_296"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_296" result="shape"/>
</filter>
</defs>
`,
  },
  s: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_299)">
<path d="M10 6V13H6L12 19L18 13H14V6H10Z" fill="white"/>
<path d="M13 7V14H15.59L12 17.59L8.41 14H11V7H13Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_299" x="4.2" y="5.2" width="15.6" height="16.6" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.9"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.65 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_299"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_299" result="shape"/>
</filter>
</defs>
`,
  },
  e: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_290)">
<path d="M13 18L19 12L13 6V10H6V14H13V18Z" fill="white"/>
<path d="M7 11H14V8.41L17.59 12L14 15.59V13H7V11Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_290" x="4.2" y="5.2" width="16.6" height="15.6" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.9"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.65 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_290"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_290" result="shape"/>
</filter>
</defs>
`,
  },
  w: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_300)">
<path d="M5 12L11 18V14H18V10H11V6L5 12Z" fill="white"/>
<path d="M17 13H10V15.59L6.41 12L10 8.42001V11H17V13Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_300" x="3.2" y="5.2" width="16.6" height="15.6" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.9"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.65 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_300"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_300" result="shape"/>
</filter>
</defs>
`,
  },
  ne: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_293)">
<path d="M17.61 14.49V6H9.12L11.95 8.83L7 13.78L9.83 16.61L14.78 11.66L17.61 14.49Z" fill="white"/>
<path d="M8.41 13.78L13.36 8.83L11.54 7H16.61V12.07L14.78 10.24L9.83 15.19L8.41 13.78Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_293" x="5.2" y="5.2" width="14.21" height="14.21" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.9"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.65 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_293"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_293" result="shape"/>
</filter>
</defs>
`,
  },
  nw: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_295)">
<path d="M6 6V14.48L8.83 11.66L13.78 16.61L16.61 13.78L11.66 8.83L14.49 6H6Z" fill="white"/>
<path d="M13.78 15.19L8.83 10.24L7 12.07V7H12.07L10.24 8.83L15.19 13.78L13.78 15.19Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_295" x="4.2" y="5.2" width="14.21" height="14.21" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.9"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.65 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_295"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_295" result="shape"/>
</filter>
</defs>
`,
  },
  se: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_297)">
<path d="M7 9.83L11.95 14.78L9.12 17.61H17.61V9.12L14.78 11.95L9.83 7L7 9.83Z" fill="white"/>
<path d="M9.83 8.41L14.78 13.36L16.61 11.54V16.61H11.54L13.36 14.78L8.41 9.83L9.83 8.41Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_297" x="5.2" y="6.2" width="14.21" height="14.21" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.9"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.65 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_297"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_297" result="shape"/>
</filter>
</defs>
`,
  },
  sw: {
    width: 24,
    height: 24,
    viewBox: '0 0 24 24',
    markup: `
<g filter="url(#filter0_d_2_298)">
<path d="M8.83 11.95L6 9.12V17.6L14.48 17.61L11.66 14.78L16.61 9.83L13.78 7L8.83 11.95Z" fill="white"/>
<path d="M15.19 9.83L10.24 14.78L12.07 16.61L7 16.6V11.53L8.83 13.36L13.78 8.41L15.19 9.83Z" fill="black"/>
</g>
<defs>
<filter id="filter0_d_2_298" x="4.2" y="6.2" width="14.21" height="14.21" filterUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
<feFlood flood-opacity="0" result="BackgroundImageFix"/>
<feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha"/>
<feOffset dy="1"/>
<feGaussianBlur stdDeviation="0.9"/>
<feColorMatrix type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.65 0"/>
<feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow_2_298"/>
<feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow_2_298" result="shape"/>
</filter>
</defs>
`,
  },
};

const rotateAngles: Record<ResizeDirection, number> = {
  n: 0,
  ne: 45,
  e: 90,
  se: 135,
  s: 180,
  sw: 225,
  w: 270,
  nw: 315,
};

function CursorSvg({
  className,
  rotate,
  definition,
}: {
  className?: string;
  rotate?: number;
  definition: CursorSvgDefinition;
}) {
  return (
    <svg
      className={cn('app-cursor', className)}
      width={definition.width}
      height={definition.height}
      viewBox={definition.viewBox}
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      style={rotate !== undefined ? { transform: `rotate(${rotate}deg)` } : undefined}
      dangerouslySetInnerHTML={{ __html: definition.markup }}
    />
  );
}

function DefaultCursor() {
  return <CursorSvg className="app-cursor--default" definition={cursorSvgDefinitions.arrow} />;
}

function PointerCursor() {
  return <CursorSvg className="app-cursor--pointer" definition={cursorSvgDefinitions.pointer} />;
}

function TextCursorIcon() {
  return <CursorSvg className="app-cursor--text" definition={cursorSvgDefinitions.text} />;
}

function NotAllowedCursor() {
  return (
    <CursorSvg className="app-cursor--not-allowed" definition={cursorSvgDefinitions.notAllowed} />
  );
}

function ResizeCursor({ direction }: { direction: ResizeDirection }) {
  return (
    <CursorSvg
      className="app-cursor--resize"
      definition={resizeSvgDefinitions[direction]}
    />
  );
}

function MoveCursor() {
  return <CursorSvg className="app-cursor--move" definition={cursorSvgDefinitions.move} />;
}

function GrabCursor({ active }: { active?: boolean }) {
  return (
    <CursorSvg
      className={cn('app-cursor--grab', active && 'app-cursor--grab-active')}
      definition={active ? cursorSvgDefinitions.grabbed : cursorSvgDefinitions.grab}
    />
  );
}

function RotateCursor({ direction }: { direction: ResizeDirection }) {
  return (
    <CursorSvg
      className="app-cursor--rotate"
      rotate={rotateAngles[direction]}
      definition={cursorSvgDefinitions.rotate}
    />
  );
}

export function getCursorComponent(variant: CursorVariant) {
  if (variant.startsWith('rotate-')) {
    const direction = variant.replace('rotate-', '') as ResizeDirection;
    return <RotateCursor direction={direction} />;
  }
  if (variant.endsWith('-resize')) {
    const direction = variant.replace('-resize', '') as ResizeDirection;
    return <ResizeCursor direction={direction} />;
  }
  switch (variant) {
    case 'pointer':
      return <PointerCursor />;
    case 'text':
      return <TextCursorIcon />;
    case 'not-allowed':
      return <NotAllowedCursor />;
    case 'grab':
      return <GrabCursor />;
    case 'grabbing':
      return <GrabCursor active />;
    case 'move':
      return <MoveCursor />;
    case 'ew-resize':
      return <ResizeCursor direction="e" />;
    case 'default':
    default:
      return <DefaultCursor />;
  }
}
